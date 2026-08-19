---
name: bookings-service
description: Use when working on Bookings — ticket reservation, booking creation, the Booking or Ticket aggregate, Redis distributed locks, the transaction pipeline, domain event dispatch, or anything in Bookings.Domain, Bookings.Application, Bookings.Sql or Bookings.Api.
---

# Bookings Service

Bookings owns tickets and the booking lifecycle. A ticket is reserved (Redis, short TTL) before it
is booked (Postgres, durable), so the expensive consistency work happens only for reservations that
actually convert.

## Scope

Covers `Bookings.Domain/`, `Bookings.Application/`, `Bookings.Sql/`, `Bookings.Api/`.

**This service is layered Clean Architecture with DDD.** Users.Api is vertical slice — do not carry
its conventions here, or these there.

**Load alongside this skill:**
- `cqrs` — before writing a command, handler or pipeline behavior.
- `efcore` — before writing a query, save, entity configuration or migration.
- `messaging` — before publishing or consuming an integration event.

The DDD section below is written self-contained; Events uses the same architecture and can lift it.

## Project layout and dependency direction

```
Bookings.Api  ──►  Bookings.Application  ──►  Bookings.Domain
      │                                            ▲
      └──────────►  Bookings.Sql  ─────────────────┘
```

| Project | Owns | Must not reference |
|---|---|---|
| `Bookings.Domain` | Aggregates, entities, domain events, repository *interfaces*, DDD primitives in `Abstractions/` | Anything. No EF, no MediatR types beyond the notification marker |
| `Bookings.Application` | Commands, handlers, application services, integration event handlers, DI wiring | `Bookings.Sql` |
| `Bookings.Sql` | `DbContext`, configurations, repository implementations, interceptors, pipeline behaviors | `Bookings.Api` |
| `Bookings.Api` | Controllers, HTTP concerns | `Bookings.Domain` directly for business decisions |

`Program.cs` calls `AddInfrastructureServices` then `AddApplicationServices`.

## DDD rules

1. **An aggregate root is the only entry point to its contents.** Load it, change it through its
   methods, save it. Nothing outside reaches past the root.
2. **Never expose a mutable collection from an aggregate.** A public `List<T>` lets callers add and
   remove behind the root's back, so invariants the root enforces mean nothing. Expose
   `IReadOnlyCollection<T>` over a private backing field and mutate only through methods.
3. **Entity state changes go through behavior, not setters.** Public setters on domain entities make
   every caller a potential source of invalid state. Model the operation (`Book()`, `Cancel()`),
   not the field.
4. **One transaction changes one aggregate.** If an operation must change two, the second changes
   via a domain event or an eventually-consistent follow-up — not by mutating another aggregate's
   entity in the same handler.
5. **Domain events reference other aggregates by id, never by instance.** Passing entity instances
   invites exactly the cross-aggregate mutation rule 4 forbids.
6. **Domain events are raised inside the aggregate**, at the point the state change happens — not
   assembled by the handler afterwards. The aggregate knows when something meaningful occurred.
7. **Domain events are cleared once dispatched.** An aggregate that keeps its events republishes
   them on the next save in the same context.
8. **Repositories are per aggregate root**, expose intention-revealing methods, and return
   aggregates. No `IQueryable` leaks out — that hands query construction to the caller and defeats
   the boundary.
9. **The domain project stays persistence-ignorant.** No EF attributes, no `DbContext`, no
   navigation shapes chosen for the ORM's benefit. Mapping is `Bookings.Sql`'s job.

## Staying in sync with the events catalogue

`Bookings.Application/EventSync` holds the commands and handlers that bring tickets back in line
with a change published by Events; `IntegrationEventHandlers` holds the thin Wolverine `Consume`
handlers that translate each contract into one of them.

| Contract | Effect on tickets |
|---|---|
| `EventCreated` | bulk-create one ticket per seat |
| `EventRescheduled` | move `EventDate` on every ticket for the event |
| `EventCancelled` | set `Status = Cancelled`; never delete — a booking that pointed at them still has to be explicable |
| `EventRelocated` | reconcile: move surviving seats, cancel seats that no longer exist, create tickets for seats that are new |

1. **`Ticket.EventVersion` is how far that ticket has been brought in line.** `IsStale(version)`
   treats equal-or-lower as stale, and `Reschedule`/`Relocate`/`Cancel` each guard themselves rather
   than trusting the caller — a new consumer cannot reintroduce the bug by forgetting to check.
2. **`ReconcileEventVenueCommandHandler` also rejects stale messages at the message level**, against
   the highest version already applied. It is the only handler that *creates* tickets, and a seat that
   does not exist yet has no version to compare against.
3. **A cancelled ticket does not count as covering its seat.** If a seat leaves the event and later
   returns, the holder has already been told their ticket is void, so the seat gets a fresh ticket
   rather than the old one quietly coming back to life. Two rows for one seat — one cancelled, one
   active — is the intended outcome.
4. **New slices colocate command and handler in one namespace**, because `ColocationTest` requires it.
   The four handlers in `Bookings.Application/CommandHandlers` predate the rule and still fail it.
5. **Consume handlers stay `public`** so Wolverine discovers them; the MediatR handlers behind them
   are `internal` per `VisibilityTest`, which is why `Bookings.Application.csproj` carries
   `InternalsVisibleTo` for the test project.

**Known loose end:** a relocation can cancel already-booked tickets, leaving the parent `Booking`
pointing at cancelled tickets. Refunds, notifications and booking-level cancellation are not built —
the tickets are cancelled and nothing else happens. Any work on `Booking` cancellation should start
here.

## The reservation and booking flow

```
POST reserve                          POST book
  │                                     │
  ▼                                     ▼
ReserveTicketCommand                  MakeBookingCommand
  │ acquire distributed lock            │ read reservations from Redis
  │ check Redis for existing            │ validate event + user + ticket ids
  │ write reservations, TTL 5 min       │ load tickets from Postgres
  │ release lock                        │ create Booking aggregate
                                        │ raise BookingCreatedDomainEvent
                                        │ save → interceptor dispatches
```

Reservation is deliberately not durable: an abandoned checkout expires by TTL instead of needing
compensation. Booking is durable and transactional.

## Locking and caching rules

10. **Lock the narrowest thing that needs locking.** A lock key must identify the contended
    resource — the ticket, or the event — never a constant. One shared key serializes every
    reservation in the system into a single-file queue.
11. **The check and the write both happen inside the lock.** Reading "is this reserved?" outside the
    lock and writing inside it is the same race with extra steps.
12. **A lock has a TTL shorter than the work it protects is allowed to take**, and the code must be
    correct if the lock expires mid-operation. Distributed locks are an optimisation over a
    correctness check, not a replacement for one.
13. **Redis writes do not roll back.** Anything written to Redis inside a database transaction
    survives that transaction's rollback. Don't rely on a transaction to undo cache state.
14. **Reservation TTL is a business rule.** It belongs in configuration, not a literal buried in a
    handler.

## Transactions and domain event dispatch

15. **One transaction owner.** `TransactionBehavior` (MediatR pipeline) and Wolverine's EF Core
    transactional middleware both want to own the transaction around a `DbContext`. Running both
    over the same context is a conflict — choose one and disable the other.
16. **A pipeline behavior that opens a database transaction must not apply to requests that never
    touch the database.** An open-generic registration hits every command, including
    Redis-only ones.
17. **Anything a domain event handler changes must still be saved.** `DomainEventPublisherInterceptor`
    dispatches from `SavedChangesAsync`, which runs *after* the write. State a handler mutates at
    that point sits in the change tracker unsaved. Either dispatch before saving, or have the
    handler persist its own change explicitly.
18. **Never block on async inside an interceptor.** `.GetAwaiter().GetResult()` in the synchronous
    `SavingChanges` override is a deadlock waiting for the right synchronization context.

## Known gaps

Current code does not match the rules above.

**Stops the service from running at all:**
- `appsettings.json` has no `ConnectionStrings` section, but `DefaultConnection`, `Redis` and
  `RabbitMQ` are all read with `!`. Startup throws.
- `ConfigureRabbitMq` passes a resolved connection *string* to `UseRabbitMqUsingNamedConnection`,
  which expects a connection *name* (see `messaging`).
- Controllers are never mapped — no `AddControllers()`, no `MapControllers()` in `Program.cs`.
- `IDistributedLockProvider` is a singleton resolving scoped `IDatabase` from the root provider.
  With scope validation on (the default in Development) this throws on first resolution.
- `AddApplicationServices` calls `services.BuildServiceProvider()` inside the Redis cache factory,
  building a second container.

**Wrong behavior:**
- `ReserveTicketCommandHandler` locks on the literal `"SomeUniqKey"` (rule 10). Correctness
  currently depends on that global lock; narrowing the key without re-checking rule 11 reintroduces
  the race.
- `BookingCreatedDomainEventHandler` sets `ticket.Status = Booked` and nothing persists it
  (rule 17). Tickets never become Booked.
- `TransactionBehavior` is registered open-generic, so `ReserveTicketCommand` opens a Postgres
  transaction for pure Redis work (rules 13, 16).
- `Policies.UseDurableLocalQueues()` leaves RabbitMQ endpoints non-durable (see `messaging`).
- `TransactionBehavior` and Wolverine's EF transactions both wrap the same `DbContext` (rule 15).

**Domain modelling:**
- `Booking.BookedTickets` and `BookingHistories` are public mutable `List<>` (rule 2).
- `Ticket` has public setters throughout (rule 3) and is mutated by another aggregate's event
  handler (rules 4, 5).
- `BookingCreatedDomainEvent` carries `Ticket[]` instances rather than ids (rule 5).
- `Booking.Status` is `init`, so a booking can never legally change state.
- `Entity` has no `ClearDomainEvents` (rule 7) and no identity equality.
- `IAggregateRoot` is an empty marker that nothing enforces.

**Incomplete:**
- `IRequestManager` has no implementation, so `IdentifiedCommandHandler` fails on first use — the
  idempotency mechanism is inert.
- `EventsService.GetEventByIdAsync` throws `NotImplementedException`.
- `TransactionBehavior` logs with `Console.WriteLine`.
- `BaseController.UserId` is an unused `protected long`; nothing reads the gateway's
  `X-Identity-UserId` header, so no request is attributed to a real user.
- `BookingDomainContext` takes non-generic `DbContextOptions` (`efcore` rule 14).
- Repositories use `AddAsync`/`AddRangeAsync` (`efcore` rule 1) and each expose their own
  `SaveChangesAsync`, so `IUnitOfWork` is implemented twice over one context.
- `CacheService` uses Newtonsoft.Json while the rest of the stack is on System.Text.Json.

## Adding a feature

1. Model the change on the aggregate in `Bookings.Domain` — a method, not a setter. Raise the
   domain event there.
2. Add the command record in `Bookings.Application/Commands`.
3. Add the handler in `Bookings.Application/CommandHandlers` — `internal`, one operation.
4. If it needs new persistence, add the repository method to the interface in `Bookings.Domain` and
   implement it in `Bookings.Sql`.
5. Add the controller action in `Bookings.Api` — dispatch and map only.
6. If another service must learn about it, translate to an integration event in
   `TicketMaster.Common` and publish through the outbox (see `messaging`).

## Common mistakes

| Symptom | Cause |
|---|---|
| All reservations serialize behind each other | Constant lock key (rule 10) |
| Two users reserve the same ticket | Check performed outside the lock (rule 11) |
| Domain event handler's changes vanish | Dispatched after save, never persisted (rule 17) |
| Redis state survives a failed booking | Redis doesn't roll back (rule 13) |
| Transaction opened for a cache-only command | Open-generic behavior on every request (rule 16) |
| Aggregate invariant violated with no code path to blame | Mutable collection or public setter (rules 2, 3) |
| Events republished on a second save | Domain events never cleared (rule 7) |
