---
name: rpc
description: Use when adding or changing a synchronous service-to-service call in TicketMaster — writing or editing a .proto contract, wiring a gRPC client or server, setting deadlines, handling RpcException — or when deciding whether a cross-service call should be an RPC or an integration event. Covers gRPC over HTTP/2 with Protobuf, which the Bookings to Events call uses.
---

# Synchronous Service Calls

One service asks another a question and waits for the answer. The wait is the whole cost: the
caller's latency, availability and failure modes become the callee's as well. Every rule here exists
to keep that coupling in the places where it was chosen on purpose.

## Scope

Cross-cutting — any service that calls another and cannot continue without the reply.

One call site is built — Bookings → Events, described below. The gateway → Users hop is still HTTP
and is a decision, not a description of code you can go and read.

**Load alongside:** `messaging` — it owns the other half of the choice and must not be restated here.
`cqrs` when the call happens inside a handler. `testing` for how the seam is tested.
gRPC, Protobuf and MSBuild specifics are in `grpc-reference.md` in this directory. **The rules below
are written for the pattern, not the library**, so replacing gRPC changes that file rather than these
rules.

## Choose the transport before anything else

> **If an integration event already carries the fact, read your own copy. Do not call the owner.**

RPC is reserved for questions no event can answer:

| The caller needs | Use |
|---|---|
| A live decision with no event behind it — "is this token still valid?" | **RPC** |
| An answer that depends on the caller's arguments — a lookup, a search, a computed quote | **RPC** |
| The owner's state *now*, where acting on a stale copy would be wrong | **RPC** |
| A fact the owner already announces — event created, rescheduled, relocated, cancelled | **its own replica**, fed by `messaging` |
| To tell another service something happened | **an integration event** |
| To have work done that the caller does not wait for | **an integration event** |

The rule is not about latency, and RPC is not "the fast option". Both transports are fast. The
difference is that a message leaves the caller working when the other service is down, and an RPC
does not. Spend that only where the list above says it is unavoidable.

### The call sites

1. **Gateway → Users, token introspection.** `UsersServiceAuthHandler` calls
   `api/users/auth?token=...` on every request that reaches the gateway. Nothing announces "this
   token is valid"; it is a live decision, so it is RPC by the rule above, and it is the hottest
   service-to-service call in the system.

   Worth knowing before optimising it: validating the JWT signature at the gateway would remove the
   hop entirely rather than make it cheaper. That is a larger change and has not been decided.

2. **Bookings → Events, venue and seat validation.** `CreateTicketCommandHandler` calls
   `IEventsService.GetEventByIdAsync`, implemented over gRPC against `events.v1.EventsLookup`.
   **This is the built one, and it is the interesting case**, because the rule above says not to
   call the owner for facts an integration event already carries — and `EventCreatedIntegrationEvent`
   does carry the venue and seats.

   It is an RPC anyway because `POST /api/tickets` is the admin's repair tool. Validating a repair
   against the local replica is circular: a lost or unprocessed `EventCreated` is the likeliest
   reason inventory is wrong, so the replica would refuse the repair exactly when the replica is the
   broken part. A repair path validates against the authoritative source.
   `CreateTicketTests.Creates_a_seat_for_an_event_the_replica_has_never_heard_of` pins that.

   **This is the shape of the exception, not a loophole.** It applies to a path whose job is to fix
   the replica. Ordinary read paths still read the replica — `ReconcileEventVenueCommandHandler` and
   the rest of `EventSync` do not call Events and must not start.

   Three things that fall out of it:

   - `CreateTicketCommand` is deliberately **not** `ITransactionalRequest`, per rule 1. Its single
     `SaveChangesAsync` is atomic on its own and creating a ticket raises no domain event, so nothing
     needs the outer transaction.
     `TransactionBehaviorRegistrationTests.Leaves_the_admin_ticket_create_alone` fails if it goes
     back.
   - Bookings keeps the duplicate check locally. Events can say the seat is real; only Bookings knows
     whether it has already sold it — `ITicketsRepository.SeatIsCoveredAsync`.
   - Creating a ticket now needs Events reachable. Unreachable becomes `EventsUnavailableException`,
     mapped to **503**, not to the 409 its base class would otherwise give it.

## Rules

1. **Never make an RPC call inside an open database transaction.** `TransactionBehavior` opens a
   Postgres transaction around every `ITransactionalRequest`, and `CreateTicketCommand` is one. A
   network hop inside it holds a connection and its row locks for the duration of another service's
   latency — including that service's own timeouts and retries. Resolve the call *before* entering
   the command, or make the command non-transactional. This is the rule most likely to be broken by
   accident, because the transaction is invisible at the call site.

2. **Every call sets a deadline.** There is no ambient timeout the way `HttpClient` has one; an
   unset deadline means wait forever. Set it at the call, from the caller's own budget, and make it
   shorter than whatever budget the caller is itself working to.

3. **Propagate the caller's deadline and cancellation token into the call.** A service handling a
   request that is already 400ms into a 500ms budget must not start a fresh 5s call. Pass the
   `CancellationToken` the handler was given, and let the deadline shrink as it is propagated —
   never grow.

4. **A failed RPC is an expected outcome, not an exception case.** Callee down, deadline exceeded,
   connection refused — each needs a decided answer: fail the caller's operation with a clear error,
   or degrade to something the caller can do without it. "It will usually be up" is not a decision.

5. **Do not retry a call that is not idempotent**, and do not retry at more than one layer. A read
   is safe to retry; anything that changes state on the other side is not, unless the callee
   deduplicates on a key the caller supplies. Retries stacked at two layers multiply.

6. **The transport type never leaves the infrastructure boundary.** A handler depends on the
   calling service's own interface, never on a generated client; the generated client is one
   implementation of that interface, registered in DI like any other. In Bookings that shape already
   exists — an interface in `Bookings.Application/Services/Interfaces` with its implementation beside
   it in `Implementations/`, which is how `ICacheService` is arranged. A handler that catches a
   transport exception type has leaked.

7. **Translate transport failures into the caller's own exceptions at that boundary.** A missing
   event is `NotFoundException`, the same as it would be from a local lookup. The `Bookings.Api`
   exception-to-status mapping then works unchanged, and handlers keep catching what they already
   catch.

8. **Contracts are public and evolve additively** — same rule as integration events, same reasoning:
   producer and consumer deploy separately, so both versions run at once. Add optional fields; never
   remove, renumber or retype an existing one. `grpc-reference.md` has the exact Protobuf
   compatibility rules, which are stricter and less intuitive than they look.

9. **Contracts carry primitives and DTOs, never domain entities.** Shipping an entity couples the
   caller to the callee's model and drags its graph onto the wire.

10. **A call across a service boundary is not a substitute for owning data.** If a service finds
    itself calling another inside a loop, or calling to fill in a field on every row it returns, the
    boundary is in the wrong place. Fix the boundary; do not cache your way around it.

## Where things live

Contracts go in `TicketMaster.Common/Protos/`, beside `IntegrationEvents/`, for the same reason
those live there: the thing crossing a service boundary is shared, so producer and consumer compile
against one copy.

`TicketMaster.Common` itself generates nothing. Each project declares only the half it plays:

```xml
<!-- Events.Api.csproj — implements the contract -->
<Protobuf Include="..\TicketMaster.Common\Protos\events.proto" GrpcServices="Server" />

<!-- Bookings.Application.csproj — calls it -->
<Protobuf Include="..\TicketMaster.Common\Protos\events.proto" GrpcServices="Client" />
```

So no project carries generated code for a role it does not play, and the file is still shared rather
than copied. Generated sources land under `obj/` — never commit them, never edit them.

Package versions (`Grpc.AspNetCore`, `Grpc.Net.ClientFactory`, `Grpc.Tools`) go in the root
`Directory.Packages.props`, never as `Version=` on the reference — central package management is on.

## Testing

Do not restate `testing`; it governs. What it means here:

- **Stubbing the outbound interface is correct**, and is the one exception to Bookings' no-fakes
  rule: a call to another process is not the same as `ICacheService` or `IDistributedLockProvider`,
  where a hand-written fake restates the assumption under test. `StubEventsService` in
  `BookingIntegration/Fixtures` is that stub — the fixture registers it after
  `AddApplicationServices`, so the production wiring still runs as written and only the last hop is
  replaced.
- **Do not test generated clients or stubs.** That tests the code generator.
- **The one thing worth an integration test is the error round-trip**: a domain exception raised in
  the callee arrives at the caller as the same domain exception, through whatever translation rule 7
  puts at the boundary. It is the only part of the seam that is hand-written on both ends, and the
  part that silently degrades to "everything is a 500" when it breaks.
- Adding a project means updating that architecture suite's `BaseTest.cs` to load the new assembly.

## Not covered, deliberately

Considered and excluded, so a later reader knows they were not forgotten:

- **Streaming calls** — no use case in this system. Both intended call sites are single
  request/response.
- **gRPC-Web and JSON transcoding** — needed only for browser clients. Service-to-service traffic is
  internal, and the public edge stays REST through YARP.
- **mTLS and service-to-service credentials** — no service identity scheme exists yet. Identity today
  travels as the `X-Identity-*` headers the gateway sets, and extending that across an RPC hop is an
  open question, not a decided one.
- **Client-side load balancing** — one instance per service today.

## Common mistakes

| Symptom | Cause |
|---|---|
| Connection pool exhaustion, lock waits, deadlocks under load | An RPC call inside a `TransactionBehavior` transaction (rule 1) |
| A hung request that never returns | No deadline set — there is no default (rule 2) |
| One slow service turns into a system-wide stall | Deadlines not propagated, so each hop restarts the clock (rule 3) |
| Every cross-service failure surfaces as a 500 | Transport exceptions escaping the infrastructure boundary (rules 6, 7) |
| A duplicated side effect after a timeout | Retrying a non-idempotent call (rule 5) |
| Consumers break on deploy after a contract edit | A field removed, renumbered or retyped (rule 8) |
| Chatty N+1 traffic between two services | The service boundary is wrong (rule 10) |
