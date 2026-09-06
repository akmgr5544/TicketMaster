---
name: messaging
description: Use when publishing or consuming integration events between TicketMaster services, changing broker topology, or touching the outbox. Covers Wolverine (UseWolverine, PersistMessagesWithPostgresql, conventional routing, Consume handlers) and RabbitMQ as the current implementation.
---

# Integration Messaging

Services communicate across boundaries by publishing integration events through a broker. A message
is only trustworthy if it was written in the same transaction as the state change it describes, and
only safe if the consumer can process it twice.

## Scope

Cross-cutting — every TicketMaster service that publishes or consumes across a service boundary.

Events publishes four contracts — `EventCreated`, `EventRescheduled`, `EventRelocated`,
`EventCancelled` — all through `Events.Application/IntegrationEvents/IIntegrationEventPublisher`.
Bookings consumes all four in `Bookings.Application/IntegrationEventHandlers`, translating each to a
command in the `EventSync` slice. **Events still has no outbox at all, and Bookings' is enrolled but
unproven** (see the Durability section
and the Events skill), so rule 4 does not hold in practice today.

**The rules below are written for the pattern, not the library.** Wolverine and RabbitMQ specifics
live in their own sections, so replacing either changes those sections rather than the rules.

**Load alongside:** `efcore` when the outbox shares a transaction with a `DbContext`.
`rpc` when deciding whether a cross-service interaction should be a message at all — it holds the
rule, and the short version is that a message is the default and an RPC is the exception.
For broker-level concepts — exchange types, bindings, dead lettering, quorum queues, acks,
prefetch — read `rabbitmq-reference.md` in this skill directory.

## Domain events vs integration events

These are different things and must not be conflated.

| | Domain event | Integration event |
|---|---|---|
| Scope | Inside one service | Across service boundaries |
| Transport | In-process dispatch | Broker |
| Contract | Private, change freely | Public, versioned |
| Lives in | The owning service's Domain project | `TicketMaster.Common/IntegrationEvents` |
| Carries | Domain types | Primitives and DTOs only |

A domain event that needs to leave the service is *translated* into an integration event. It is
never published to the broker directly.

## Rules

1. **Integration event contracts live in `TicketMaster.Common/IntegrationEvents`** so producer and
   consumer compile against the same type. Never define one privately in a service.
2. **Contracts carry primitives and DTOs — never domain entities.** Shipping an entity couples the
   consumer to the producer's model and drags its whole graph onto the wire.
3. **Evolve contracts additively.** Add optional members; never remove or retype an existing one.
   Producer and consumer deploy separately, so both versions run at once.
4. **A message that describes a state change must be written in the same transaction as that
   change** — that's the outbox. Publishing after the transaction commits loses the message on
   crash; publishing before it commits sends a message about something that never happened.
5. **Consumers must be idempotent.** Delivery is at-least-once. Processing the same message twice
   must produce the same end state — key on the message id or a natural business key.
6. **Consumers must tolerate out-of-order arrival.** Retries and redelivery reorder messages. Check
   whether the change is still applicable rather than assuming a sequence. Two things make this
   tractable, and the Events → Bookings flow uses both:

   - **Carry resulting state, not deltas.** `EventRelocated` says which seats the event now has, not
     which were added or removed. Applying it twice lands in the same place, so redelivery is free.
     A delta (`SeatsAdded`) is wrong under both redelivery and reorder.
   - **Carry the producing aggregate's version, and have the consumer reject anything not newer.**
     `Ticket.EventVersion` records how far each ticket has got; `Ticket.IsStale(version)` treats
     equal-or-lower as stale, and each mutator guards itself so a new consumer cannot forget.
     Equal counts as stale because that is the same message arriving twice.

   **A guard on the entity is not enough when a message can create rows.** The venue reconcile also
   rejects the whole message up front, comparing against the highest version already applied — a seat
   that does not exist yet has no version to compare against, so a stale message would re-add seats a
   newer one removed. Wherever a consumer inserts rather than only updating, check at the message
   level too.
7. **Every listener has an explicit failure policy and a terminal destination.** Decide what retries
   (transient faults), what goes straight to a dead letter queue (malformed, unprocessable), and
   what is discarded. Unbounded retry on a poison message blocks the queue behind it.
8. **One handler per message type.** A handler that switches on message content is two handlers.
9. **Auto-provisioning is a development convenience.** It creates topology from whatever the code
   declares at boot. In production, topology is deliberate — declared, reviewed and versioned.
10. **The broker is not a database.** Messages carry the facts the consumer needs; a consumer that
    must call back to the producer for the rest indicates the contract is too thin.

## Wolverine today

WolverineFx 5.31.1, configured in `Bookings.Application.Extensions.ConfigureRabbitMq`.

```csharp
hostBuilder.UseWolverine(options =>
{
    options.UseRabbitMqUsingNamedConnection("RabbitMQ")   // a NAME, not a connection string
        .AutoProvision()
        .UseConventionalRouting();

    options.PersistMessagesWithPostgresql(connectionString);
    options.UseEntityFrameworkCoreTransactions();

    options.Policies.UseDurableLocalQueues();
    options.Policies.UseDurableInboxOnAllListeners();
    options.Policies.UseDurableOutboxOnAllSendingEndpoints();
});
```

**`UseRabbitMqUsingNamedConnection` takes a connection *name*.** Wolverine calls
`IConfiguration.GetConnectionString()` on it itself. Resolving the string first and passing that in
makes Wolverine look up a connection string named after the whole AMQP URI.

### Durability — read this before trusting the outbox

`PersistMessagesWithPostgresql` only creates the storage. It does not enrol any endpoint.

| Call | Covers |
|---|---|
| `Policies.UseDurableLocalQueues()` | **Local in-process queues only** |
| `Policies.UseDurableInboxOnAllListeners()` | All listening endpoints, including broker queues |
| `Policies.UseDurableOutboxOnAllSendingEndpoints()` | All outgoing endpoints |
| `.UseDurableInbox()` / `.UseDurableOutbox()` | One endpoint |

The conventional-routing docs state that listener endpoints are created "without durable outbox
enrollment." `UseDurableLocalQueues()` alone therefore leaves all RabbitMQ traffic non-durable, which
is what Bookings ran with until the inbox and outbox policies were added alongside it. All three are
applied now, so rule 4 holds on the Bookings side.

**Enrolment is observed, not assumed.** `BookingIntegration`'s `BookingsHostFixture` boots the real host —
`Program.cs` unmodified, `ConfigureRabbitMq` included — against Postgres, Redis and RabbitMQ
containers, and asserts every application RabbitMQ endpoint came up in `EndpointMode.Durable`.
Verified by mutation: removing `UseDurableInboxOnAllListeners()` turns
`Every_broker_listener_is_durable` red. The project's other fixture still never calls
`ConfigureRabbitMq`, and should not — that is what keeps the other 113 tests fast.

### Conventional routing

`UseConventionalRouting()` derives topology from discovered handlers:

- **Incoming:** a durable queue named from the message type, with a single inline listener.
- **Outgoing:** a **fanout** exchange named after the message type alias, created on demand.

Customisation:

```csharp
.UseConventionalRouting(x =>
{
    x.ExchangeNameForSending(type => type.Name + "Exchange");
    x.QueueNameForListener(type => type.FullName!.Replace('.', '-'));
    x.ConfigureListeners((listener, context) => { });
    x.ConfigureSending((ex, _) => { });
})
```

Because outgoing exchanges are fanout, every queue bound to one gets a copy. That is the right
default for events and the wrong default for commands.

### Handlers

A Wolverine handler is a plain class with a `Handle` or `Consume` method taking the message type.
No interface, no registration.

```csharp
public class EventCreatedIntegrationEventHandler
{
    public async Task Consume(EventCreatedIntegrationEvent message, CancellationToken ct) { }
}
```

### Error handling

Policies are evaluated message-type specific → global → chain policy.

```csharp
opts.OnException<SqlException>()
    .RetryWithCooldown(50.Milliseconds(), 100.Milliseconds(), 250.Milliseconds())
    .WithFullJitter();

opts.OnException<TimeoutException>().MoveToErrorQueue();
opts.OnException<UnprocessableMessageException>().Discard();
opts.OnException<DownstreamDownException>().Requeue().AndPauseProcessing(10.Minutes());
```

Per handler:

```csharp
public static void Configure(HandlerChain chain) => chain.OnException<IOException>().Requeue();
```

Circuit breaker per endpoint:

```csharp
opts.ListenToRabbitQueue("incoming").CircuitBreaker(cb =>
{
    cb.MinimumThreshold = 10;
    cb.PauseTime = 1.Minutes();
    cb.FailurePercentageThreshold = 10;
});
```

Jitter matters: `WithFullJitter()` spreads retries over `[d, 2d]` so a downstream outage doesn't
produce a synchronised retry storm.

### EF Core integration

`UseEntityFrameworkCoreTransactions()` lets Wolverine enlist an already-registered `DbContext`;
`AddDbContextWithWolverineIntegration<T>()` registers and enlists in one step. Wolverine's
transactional middleware then calls `SaveChangesAsync` **and** flushes outbox messages together.

The docs recommend registering `DbContextOptions` with `optionsLifetime: ServiceLifetime.Singleton`
— described as "a non-trivial performance optimization for Wolverine."

**Do not run a second transaction manager over the top.** A MediatR pipeline behavior that opens
its own `BeginTransactionAsync` around the same `DbContext` competes with Wolverine's middleware
for transaction ownership. Pick one.

## When Wolverine is replaced

Rules 1–10 hold as written. What changes: the host configuration, handler discovery and method
conventions, the durability policy API, the error-policy API, and the outbox's transaction
enlistment. Keeping contracts in `TicketMaster.Common` and consumers idempotent is what makes the
swap cheap — both are properties of your code, not the library.

## Common mistakes

| Symptom | Cause |
|---|---|
| Messages lost when the process dies after commit | Published outside the transaction (rule 4) |
| Consumer sees an event for a change that never committed | Published before the transaction committed (rule 4) |
| Duplicate side effects downstream | Consumer not idempotent (rule 5) |
| Queue stalls behind one bad message | No terminal failure policy (rule 7) |
| Broker connection fails at startup | Connection string passed where a connection *name* was expected |
| Outbox tables exist but messages still lost | Durability policy covers local queues only, not broker endpoints |
| Consumer breaks after a producer deploy | Contract changed non-additively (rule 3) |
| A newer change gets reverted | Consumer applied a stale message — no version guard (rule 6) |
| Redelivery duplicates rows | Consumer inserts without a message-level version check (rule 6) |
| Consumer must call the producer for a missing field | Contract too thin (rule 10) — e.g. reconciling seats needs the event's start date to create a ticket |
| Every bound queue receives a command | Conventional routing publishes to a fanout exchange |

## Sources

Wolverine docs retrieved 2026-08-08.

- Durability / outbox: https://wolverinefx.net/guide/durability/
- EF Core integration: https://wolverinefx.net/guide/durability/efcore.html
- RabbitMQ transport: https://wolverinefx.net/guide/messaging/transports/rabbitmq/
- Conventional routing: https://wolverinefx.net/guide/messaging/transports/rabbitmq/conventional-routing.html
- Error handling: https://wolverinefx.net/guide/handlers/error-handling.html
