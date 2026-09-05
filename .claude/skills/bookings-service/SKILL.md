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

### Where things go in `Bookings.Application`

Organised **by type first, then by area** — not by feature slice. Do not reorganise this into
feature folders.

```
Commands/                EventSyncCommands.cs        (no area folder)
Commands/<Area>/         Bookings/  Payments/  Tickets/
CommandHandlers/<Area>/  Bookings/  EventSync/  Tickets/
Queries/                 CustomerBookingQueries.cs
QueryHandlers/<Area>/    CustomerBookings/
Dtos/                    BookingDto, ReserveTicketDto, EventsServiceDtos/
Extensions/              ServiceCollectionExtension, TicketLockExtensions (+ ReservationKeys)
DomainEventHandlers/  IntegrationEventHandlers/  Services/  Exceptions/  Abstractions/
```

**A namespace always mirrors its folder.** Rider's *namespace does not correspond to file location*
inspection enforces this and will restore it, so a hand-kept flat namespace erodes file by file
instead of failing loudly. Follow the folders.

| Folder | Namespace |
|---|---|
| `Commands/Bookings/MakeBookingCommand.cs` | `Bookings.Application.Commands.Bookings` |
| `Commands/EventSyncCommands.cs` (no area folder) | `Bookings.Application.Commands` |
| `CommandHandlers/Bookings/MakeBookingCommandHandler.cs` | `Bookings.Application.CommandHandlers.Bookings` |
| `Queries/CustomerBookingQueries.cs` | `Bookings.Application.Queries` |

A handler is therefore never in its command's namespace and always needs a
`using Bookings.Application.Commands.<Area>;`. Several command records may share one file when they
belong to the same area (`EventSyncCommands.cs`, `PaymentCommands.cs`).

A folder named for an aggregate is fine despite appearances: `Bookings.Application.Commands.Bookings`
compiles alongside `using Bookings.Domain.Entities;` without ambiguity, as the handler namespaces
already demonstrate.

`LayoutTest` enforces both halves: a handler resides under the root its own suffix claims, so a
`QueryHandler` cannot sit among the command handlers, and every request resides under `Commands` or
`Queries`. It does not enforce which of those two, which is why `CancelBookingCommand` can live in
`Queries/CustomerBookingQueries.cs` alongside the queries it is used with.

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
4. **Commands and handlers are deliberately *not* colocated.** The commands live under `Commands/`,
   their handlers under `CommandHandlers/<Area>/`. `LayoutTest` guards that arrangement; the old
   `ColocationTest`, which required the opposite, is gone.
5. **Consume handlers stay `public`** so Wolverine discovers them; the MediatR handlers behind them
   are `internal` per `VisibilityTest`, which is why `Bookings.Application.csproj` carries
   `InternalsVisibleTo` for the test project.

**Known loose end:** a relocation can cancel already-booked tickets, leaving the parent `Booking`
pointing at cancelled tickets. Refunds, notifications and booking-level cancellation are not built —
the tickets are cancelled and nothing else happens. Any work on `Booking` cancellation should start
here.

## The reservation and booking flow

A seat is held twice over its life, and by different things. First by a Redis key that expires on its
own; then, once the booking exists, by the ticket's own status in Postgres.

```
POST reserve                    MakeBookingCommand              payment service
  │                               (no endpoint yet)                 │
  ▼                               │                                 ▼
ReserveTicketCommand              ▼                          BookingPaid / BookingPaymentFailed
  │ reject empty/over-limit/dupe  read reservations               │
  │ lock every ticket, asc. id    validate event + user + all     ▼
  │ no reservation already?       load tickets                  paid → Booking.MarkPaid → Payed
  │ tickets real and available?   Booking.Create → own event    failed → Booking.Cancel → Cancelled
  │ write reservations, TTL 5 min save → tickets Booked            │ raises BookingCancelled
  │ release locks, reverse order  after commit: drop reservation   ▼ handler → Ticket.Release → None
```

**Reserved** is a Redis key with a TTL and nothing else, so a checkout abandoned before booking needs
no compensating action — it simply lapses. **Booked** replaces that with a durable hold: the
reservation is deleted and the ticket's status carries the hold instead.

That hand-off has a consequence worth being explicit about: **after the booking is made, nothing
inside Bookings will ever release those seats on its own.** The reservation TTL no longer applies to
them. Only `BookingPaymentFailedIntegrationEvent` puts them back. If the payment service never
publishes it — for a user who simply walks away — the seats stay held indefinitely. The timeout lives
there, not here.

## Locking and caching rules

10. **Lock the narrowest thing that needs locking.** A lock key must identify the contended
    resource — never a constant. One shared key serializes every reservation in the system into a
    single-file queue, however many different events they cover. Reservation locks one key per
    ticket, `bookings:reserve:ticket:{id}` (`Locking/ReservationKeys`).
11. **Multiple locks are acquired in ascending ticket id order, never the caller's order.** That
    ordering is the only thing preventing two overlapping reservations from deadlocking: both take
    seat 7 before seat 9, so neither ends up holding what the other waits for. It lives in
    `TicketLockExtensions.TryAcquireTicketLocksAsync` with the invariant documented on it — "tidying"
    it back to request order reintroduces the deadlock.
12. **A partly-acquired set of locks is released before failing**, and duplicate ticket ids are
    rejected rather than deduplicated. These locks are not reentrant, so a repeated id waits on a
    lock the same request already holds and then fails blaming somebody else.
13. **A seat is checked against the database before it is held, not only before it is sold.**
    `Ticket.IsAvailableFor` is the rule — nobody holds it, it belongs to the event being asked about,
    and that event is still inside `Ticket.SaleGracePeriod`. Reservation reads the tickets and applies
    it, so a seat that does not exist, belongs to another event, is sold, has been paid for, or was
    cancelled with the event is refused at the reservation step. Without that, any of those reserved
    happily and failed at booking instead, having held a Redis key for the whole TTL first.
    The predicate inside `GetTicketsForBookingAsync` is the database-side mirror of the same rule; a
    query cannot call into the domain, so **if the rule changes, both change together**. Booking still
    re-checks — that is the backstop for a reservation whose TTL lapsed while its holder was buying.
14. **The check and the write both happen with every lock held.** `ReserveTicketCommandHandler` reads
    the reservation keys and writes them inside the locks it took. Reading outside them and writing
    inside is the same race with extra steps. Correctness here rests on the locks, so **anything that
    weakens them weakens the reservation itself** — this is deliberately not a conditional `SET NX`
    write, so a lock lost mid-operation is a real double-reservation window rather than a wasted
    attempt.
15. **Redis writes do not roll back.** Anything written to Redis inside a database transaction
    survives that transaction's rollback, so work aimed at Redis is queued on `IAfterCommitQueue`
    rather than done in the handler. `MakeBookingCommandHandler` deletes the reservation that way: if
    the booking then fails, the user still holds the reservation and can try again.
16. **Reservation TTL, the ticket limit and the lock wait are constants in the handlers.** `5` minutes
    and `TicketCountConfig = 2` are duplicated across the reserve and booking handlers, so a change to
    the limit has to be made in both.

## Transactions and domain event dispatch

17. **One transaction owner, and the MediatR behavior is not always it.** A Wolverine message and an
    HTTP request each get their own scope, so they never contend — the nesting is *inside* one message
    scope. `UseEntityFrameworkCoreTransactions` has Wolverine open a transaction on the scoped
    `BookingDomainContext` before it calls `Consume`, and the `_mediator.Send` inside that handler
    resolves from the same scope and so gets the same context. `TransactionBehavior` therefore defers
    when `Database.CurrentTransaction` is already set: a second transaction on that connection throws
    `"The connection is already in a transaction"` (asserted in `TransactionBehaviorTests`), and
    committing Wolverine's early would break the very guarantee it exists for. Every `Consume` handler
    sends a command, so this path is taken by all of catalogue sync and both payment outcomes — it is
    not a defensive edge case.
    After-commit work cannot run on that path, since the commit is not this behavior's to observe; it
    is logged as dropped rather than made fatal, matching how a failure on the owned path is treated.
18. **A pipeline behavior that opens a database transaction must only apply to requests that touch
    the database.** The registration is open-generic, so scoping comes from the constraint
    `where TRequest : ITransactionalRequest` — dependency injection skips an open generic whose
    constraints the requested type arguments do not satisfy (verified in `BookingIntegration`). A new
    command that writes must implement the marker, or it silently runs without a transaction.
19. **`ITransactionalRequest` lives in `Bookings.Domain/Abstractions`**, not in `Bookings.Sql`. Both
    the commands and the behavior need it, and `DependenceTest` forbids the application layer from
    referencing infrastructure.
20. **Anything a domain event handler changes must still be saved.** `DomainEventPublisherInterceptor`
    dispatches from `SavedChangesAsync`, which runs *after* the write, so state a handler mutates
    there sits in the change tracker unsaved. `BookingCreatedDomainEventHandler` saves its own change;
    the surrounding transaction is what makes that atomic with the write that triggered it. EF Core 10
    does tolerate that nested `SaveChangesAsync` — asserted in `BookingIntegration`, not assumed.
21. **Domain events are cleared before they are published, not after.** A handler that saves
    re-enters the interceptor while the aggregate is still tracked; if it still held its events they
    would publish again and that handler would run again — recursion, not a duplicate delivery.
22. **Never block on async inside an interceptor.** `.GetAwaiter().GetResult()` in a synchronous
    override is a deadlock waiting for the right synchronization context. Bookings dispatches on
    async saves only, and the sync override throws rather than silently dropping the event.

## Known gap: the admin create endpoint is not restricted

`POST /api/tickets` is meant for admins repairing inventory, and nothing enforces that.
`TicketsController.CreateTicketAsync` has no `[Authorize]`, no role and no policy, and unlike
`ReserveTicketAsync` it does not read the identity header at all. There is no role or policy usage
anywhere in `Bookings.Api`. The gateway's `GatewayAuthPolicy` requires only an *authenticated* user
for `/bookings-service/**`, so any logged-in caller can create tickets — real seats, which then
become bookable inventory.

Closing it is not a one-project change: the gateway propagates only `X-Identity-UserId` and
`X-Identity-UserName`, so no role reaches Bookings. It needs a role claim issued by Users.Api,
propagated by `AuthTransformProvider`, and enforced here. Deferred deliberately, not overlooked.

## The HTTP surface

```
POST   /api/tickets                     admin repair: create one seat, validated against Events
POST   /api/tickets/reserve             hold seats, 5 minute TTL

POST   /api/bookings                    201 + { id }
GET    /api/bookings/{id}               the caller's own
GET    /api/bookings?page=&pageSize=    the caller's own, newest first
POST   /api/bookings/{id}/cancel        204; refuses a paid booking with 400
```

27. **Identity comes from `X-Identity-UserId` and nowhere else.** `BaseController.TryGetUserId`
    reads it and the action answers 401 when it is absent — never a default, never the body. The
    request records in `Bookings.Api/Requests` deliberately carry no user id, so model binding cannot
    let a caller act as somebody else. `ReserveTicketCommand` and `MakeBookingCommand` still have a
    `UserId`, but the controller supplies it.
28. **A read scoped by caller *is* the authorization check.** `FindForUserAsync` and
    `ListForUserAsync` put the user in the query, so somebody else's booking is indistinguishable from
    one that does not exist. Do not "improve" this into a 403 — that confirms the id exists to someone
    with no business knowing.
29. **Endpoints dispatch and map.** They take `ISender`, not `IMediator`. `TicketsController` used to
    take `IMediator`; it no longer does.
30. **Exceptions map at the edge, in most-derived-first order.** `Bookings.Api/Handlers/BookingsExceptionHandler`
    maps `NotFoundException` (404), `BookingsApplicationException` (409) and `BookingsDomainException`
    (400), and returns `false` for anything else so a genuine bug stays a 500. `NotFoundException`
    derives from `BookingsApplicationException`, so reordering the switch fails the build with CS8510
    rather than quietly turning 404s into 409s. Covered by `Tests/Bookings/BookingApi`.

| Failure | Type | Status |
|---|---|---|
| Malformed request; an aggregate refused the change | `BookingsDomainException` (Domain) | 400 |
| Asked for something that is not there | `NotFoundException` | 404 |
| Well-formed, but the world says no | `BookingsApplicationException` | 409 |

`BookingException` is gone — it was one type for all three meanings, and nothing mapped it, so every
refused booking answered 500.

## Settling a booking

The payment service is not built. What exists here is the seam it publishes into: two contracts in
`TicketMaster.Common`, two `Consume` handlers, and the `Bookings.Application/Payments` slice behind
them.

| Contract | Effect |
|---|---|
| `BookingPaidIntegrationEvent` | `Booking.MarkPaid()` — `Booked → Payed`. Tickets are untouched; they were already booked. |
| `BookingPaymentFailedIntegrationEvent` | `Booking.Cancel()` — `Booked → Cancelled`, raising `BookingCancelledDomainEvent`, whose handler calls `Ticket.Release()` to put the seats back to `None`. |

23. **The two outcomes race, and whichever lands first wins.** `Cancel()` refuses a `Payed` booking
    and `MarkPaid()` refuses a `Cancelled` one, which is why the contracts need no version: a late
    failure cannot void a paid booking, and a late success cannot claim seats already back on sale.
24. **Applying the same outcome twice does nothing the second time.** Both methods return early when
    already in the target state, so no second history row is written and — importantly — `Cancel()`
    raises its event only once. Releasing twice could put back a seat somebody else has since taken.
25. **`Ticket.Release()` leaves a cancelled ticket cancelled.** A seat voided because the event itself
    was called off must not return to sale because a payment for it also failed; its holder has been
    told it is void and the seat may no longer exist. It is skipped rather than refused, so cancelling
    the booking still succeeds — this is the one place the relocation loose end below is handled.
26. **Booking-level cancellation exists only for unpaid bookings.** `Cancel()` refusing a `Payed`
    booking means refunds are still not modelled. A relocation that cancels already-booked tickets
    still leaves the parent `Booking` pointing at cancelled tickets with no notification.

## Known gaps

Current code does not match the rules above.

**Stops the service from running at all:**
- `Policies.UseDurableLocalQueues()` leaves RabbitMQ endpoints non-durable (see `messaging`).

Startup was never verified against live Postgres, Redis and RabbitMQ, so treat the above as the
known list rather than the complete one.

**Wrong behavior:**
- Nothing currently known. The three that were here — the constant lock key, the unpersisted booked
  status, and `TransactionBehavior` wrapping every request including Redis-only ones — are fixed and
  covered by tests (`ReserveTicketCommandHandlerTests`, `DomainEventDispatchTests`,
  `TransactionBehaviorTests`, `TransactionBehaviorRegistrationTests`).
- Watch rule 14 rather than treating it as settled: reservation correctness rests entirely on the
  distributed locks, so a lock lost mid-operation double-reserves a seat. This is a deliberate choice,
  not an oversight.

**Domain modelling:**
- `Booking.BookedTickets` and `BookingHistories` are public mutable `List<>` (rule 2). `Booking`'s own
  mutators are private, so nothing inside the service bypasses the root, but the hole is still open.
- `Ticket` has public setters for `Id`, `VenueId`, `EventId`, `EventDate` and `EventVersion`
  (rule 3). `Status` is now private and moves only through `Book`, `Cancel` and the sync methods.
- `Entity` has no identity equality.
- `IAggregateRoot` is an empty marker that nothing enforces.

**Incomplete:**
- `NamingConventionTest` asserted nothing for a long time — it built an ArchUnit rule and never called
  `Check(Architecture)`. Fixed, and it matches by pattern rather than suffix because reflected names
  carry the generic arity suffix (`IdentifiedCommandHandler` reports as ``IdentifiedCommandHandler`2``).
  The Events copy has the same latent trap and passes only because Events has no generic handlers.
- The architecture suite is green. There is no longer a set of expected failures to look past, so a
  red test means something actually broke.
- Nothing publishes the payment contracts. Until a payment service does, a booking stays `Booked`
  forever and its seats are never released (see **Settling a booking**).
- A command that queues after-commit work cannot be sent from a Wolverine message handler: the
  behavior does not own that transaction, so it throws rather than dropping the work. Only
  `MakeBookingCommand` queues any, and only over HTTP, so nothing hits this today.
- `IRequestManager` has no implementation, so `IdentifiedCommandHandler` fails on first use — the
  idempotency mechanism is inert.
- `EventsService.GetEventByIdAsync` is a real gRPC call now, so `CreateTicketCommand` needs Events
  reachable. It is deliberately not `ITransactionalRequest` — see the `rpc` skill.
- `Booking` has no timestamp of any kind, so the list endpoint orders by key descending as a proxy
  for "newest first" and no response can report when a booking was made. Adding `CreatedAt` needs a
  migration.
- The list endpoint returns a bare array, so a caller infers "there may be more" from receiving a full
  page. No total count and no cursor.
- The gateway sets `X-Identity-UserId` correctly now — the transform replaces rather than appends,
  `api/users/auth` exists, and the cluster addresses are filled in — but nothing tests any of it.
  Calling Bookings directly still means supplying the header by hand.
- `UserId` is a `string` throughout Bookings while `Users.Api` keys users by `long`. Aligning them
  means a migration on `Bookings.UserId`.
- Repositories use `AddAsync`/`AddRangeAsync` (`efcore` rule 1) and each expose their own
  `SaveChangesAsync`, so `IUnitOfWork` is implemented twice over one context.
- `CacheService` uses Newtonsoft.Json while the rest of the stack is on System.Text.Json.

## Adding a feature

1. Model the change on the aggregate in `Bookings.Domain` — a method, not a setter. Raise the
   domain event there, inside the method that makes the change.
2. Add the command record, and implement `ITransactionalRequest` on it if its handler writes to the
   database. Without the marker it runs with no transaction and nothing says so (rule 17).
3. Add the handler `internal sealed`, one operation, under `CommandHandlers/<Area>/` (or
   `QueryHandlers/<Area>/`), in the namespace that folder implies. Put the command itself under
   `Commands/<Area>/` in the flat `Bookings.Application.Commands` namespace.
4. If it needs new persistence, add the repository method to the interface in `Bookings.Domain` and
   implement it in `Bookings.Sql`.
   If it changes Redis, queue that on `IAfterCommitQueue` rather than doing it in the handler — the
   handler runs inside the transaction and Redis will not roll back with it (rule 15).
5. Add the controller action in `Bookings.Api` — dispatch and map only.
6. If another service must learn about it, translate to an integration event in
   `TicketMaster.Common` and publish through the outbox (see `messaging`).

## Comments

Comment the non-obvious and nothing else. A name that already says what a thing is does not need a
summary repeating it.

- **Do** explain a decision a reader would otherwise undo: why a value is what it is, why an order
  matters, why a case is handled the way it is.
- **Do not** write an XML summary for a record, a DTO, a marker interface, a constructor, or a handler
  whose name and body already say it.
- Prefer one line at the point of confusion over a paragraph above the type.

## Common mistakes

| Symptom | Cause |
|---|---|
| Reservation succeeds, booking then fails | Availability not checked at reservation (rule 13) |
| All reservations serialize behind each other | Constant lock key (rule 10) |
| Two overlapping reservations hang until they time out | Locks taken in the caller's order (rule 11) |
| A seat stays held after a reservation failed | Partial acquisition or partial write not given back (rules 12, 14) |
| Two users reserve the same ticket | Check performed outside the locks (rule 14) |
| Reservation fails claiming another user holds the seat | Duplicate ticket id in one request (rule 12) |
| Domain event handler's changes vanish | Dispatched after save, never persisted (rule 20) |
| A save recurses until the stack runs out | Domain events not cleared before publishing (rule 21) |
| "Transaction already in progress" on the EventSync path | Behavior opening a second transaction (rule 17) |
| A new command writes without a transaction | Missing `ITransactionalRequest` (rule 18) |
| Redis state survives a failed booking | Redis work not queued on `IAfterCommitQueue` (rule 15) |
| Seats never come back after an unpaid booking | Nothing publishes `BookingPaymentFailedIntegrationEvent` (rule 26) |
| A cancelled seat goes back on sale | `Release()` called on something other than a booked ticket (rule 25) |
| A late payment result overwrites a settled booking | Guards in `MarkPaid`/`Cancel` bypassed (rule 23) |
| Aggregate invariant violated with no code path to blame | Mutable collection or public setter (rules 2, 3) |
