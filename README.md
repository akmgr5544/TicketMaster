# TicketMaster

A distributed backend for high-concurrency ticket booking, built as a showcase for Clean
Architecture, Domain-Driven Design and event-driven communication on .NET 10.

**Status: 🚧 Active development.** Parts of this are deliberately incomplete, and the gaps are
listed honestly under [Known gaps](#-known-gaps) rather than left for you to discover.

## 🚀 Tech stack

| Concern | Choice |
|---|---|
| Language / runtime | C# 14, .NET 10 (`net10.0`, stable SDK — see `global.json`) |
| Architecture | Clean Architecture + DDD (Bookings, Events), vertical slice (Users) |
| CQRS | MediatR — commands, handlers, pipeline behaviors |
| Messaging | WolverineFx over RabbitMQ (Postgres-backed durable inbox/outbox in Bookings; Events has none — see [Known gaps](#-known-gaps)) |
| Relational store | PostgreSQL via EF Core (Bookings, Users) |
| Document store | Azure Cosmos DB, NoSQL API (Events) |
| Caching / locking | Redis via StackExchange.Redis + Medallion.Threading.Redis |
| Service-to-service | gRPC over HTTP/2 with Protobuf (Bookings → Events), alongside RabbitMQ |
| Edge | YARP reverse proxy with a custom authentication scheme |
| Testing | xUnit + ArchUnitNET, plus Testcontainers (Postgres + Redis) for the Bookings integration suite |

## 🏗️ Architecture

```
                       ┌────────────────────────────┐
  client ── HTTP ──►   │  TicketMaster.ApiGateway   │  (YARP reverse proxy)
                       └─────────────┬──────────────┘
                                     │  /users-service/**
                                     │  /bookings-service/**
                                     │  /events-service/**
              ┌──────────────────────┼──────────────────────┐
              ▼                      ▼                      ▼
        Users.Api            Bookings.Api             Events.Api
        (Postgres + EF,     (Postgres + EF,           (Cosmos DB,
         JWT issuer)         Redis cache + locks)      NoSQL API)
                                     ▲                      │
                                     │ consumes             │ publishes
                                     └───── RabbitMQ ◄──────┘   EventCreated
                                            (Wolverine)         EventRescheduled
                                                 ▲              EventRelocated
                                                 │ publishes    EventCancelled
                                    ┌────────────┴───────┐
                                    │  payment service   │      BookingPaid
                                    └────────────────────┘      BookingPaymentFailed
                                      not built; Bookings
                                      only consumes it
```

The gateway authenticates every request by calling Users.Api, then forwards the resolved identity
downstream as `X-Identity-UserId` / `X-Identity-UserName` headers — services read identity from
those rather than re-validating the token.

Events owns the catalogue and never learns about bookings; Bookings reacts to the catalogue and never
writes to it. Every ticket that exists does so because Events said an event exists, and every ticket
that changes does so because Events said the event changed.

## 🌐 API surface

Venues and performers each expose a conventional CRUD surface:

```
GET    /api/venues            GET    /api/performers        # cursor-paged
GET    /api/venues/{id}       GET    /api/performers/{id}
POST   /api/venues            POST   /api/performers        # 201 + { id }
PUT    /api/venues/{id}       PUT    /api/performers/{id}
DELETE /api/venues/{id}       DELETE /api/performers/{id}   # 409 if in use
```

Events deliberately differ:

```
GET    /api/events                    # cursor-paged
GET    /api/events/{id}
POST   /api/events                    # 201 + { id }
PUT    /api/events/{id}/schedule      # reschedule
PUT    /api/events/{id}/venue         # relocate — reconciles tickets downstream
PUT    /api/events/{id}/lineup        # change performers
POST   /api/events/{id}/cancel        # idempotent; no DELETE exists
```

Bookings exposes the checkout, with every action scoped to the caller the gateway resolved:

```
POST   /api/tickets                     # admin repair: one seat, validated against Events over gRPC
POST   /api/tickets/reserve             # hold seats for 5 minutes
POST   /api/bookings                    # 201 + { id }
GET    /api/bookings/{id}               # the caller's own; somebody else's is a 404
GET    /api/bookings?page=&pageSize=    # the caller's own, newest first
POST   /api/bookings/{id}/cancel        # 204; a paid booking is refused with 400
```

No request body carries a user id. Identity comes from the gateway's `X-Identity-UserId` header and
an action answers 401 without it, so a caller cannot book as somebody else by editing the body. A
booking belonging to another user answers exactly as a nonexistent one does — telling them apart
would confirm the id exists to someone with no business knowing.

Each event mutation has a different downstream consequence — relocating changes which seats exist,
rescheduling does not — so they are separate sub-resources rather than one `PUT` that would have to
infer intent by diffing. And an event is cancelled rather than deleted: tickets exist downstream, so
removal is a state transition. Collection reads return a `continuationToken`; send it back to page,
and a null token means there is nothing more. Cosmos charges for rows an `OFFSET` skips, which is why
there is no page number.

## 📦 Services

| Service | Layout | Store | Responsibility |
|---|---|---|---|
| **Users.Api** | Vertical slice (`Features/Users/…`) | Postgres | Registration, authentication, refresh tokens. Issues the JWTs and answers the gateway's introspection call. |
| **Bookings** | `Domain` / `Application` / `Sql` / `Api` | Postgres + Redis | Reservations and bookings. Owns the whole ticket lifecycle — held in Redis, sold in Postgres, settled or released when a payment result arrives — with a distributed lock per seat guarding concurrent reservation. |
| **Events** | `Domain` / `Application` / `Cosmos` / `Api` | Cosmos DB | The catalogue: events, venues, performers, each with full CRUD. Publishes `EventCreated`, `EventRescheduled`, `EventRelocated` and `EventCancelled` — the first is what causes tickets to exist in Bookings, and the rest are what keep them correct. |
| **TicketMaster.ApiGateway** | — | — | YARP routing, edge authentication, identity header propagation. |
| **TicketMaster.Common** | — | — | Integration event contracts shared across service boundaries. |

## 🔑 Patterns worth looking at

**CQRS with MediatR.** Commands in `*.Application/Commands`, handlers in `CommandHandlers`, with
open-generic `IPipelineBehavior<,>` for cross-cutting concerns.

**Two things hold a seat, at different stages (Bookings).** Reserving writes a Redis key with a TTL
and nothing else, so a checkout abandoned before booking lapses on its own and needs no compensating
action. Booking replaces that with a durable hold: the reservation is deleted and the ticket's own
status carries it. The trade is explicit — after booking, the TTL no longer applies to those seats, so
only `BookingPaymentFailed` can put them back. That timeout belongs to the payment service.

**Reservation checks the database before holding anything.** `Ticket.IsAvailableFor` is the rule —
nobody holds the seat, it belongs to the event being asked about, and that event is inside its selling
window — so a ticket that does not exist, is already sold, or was cancelled with its event is refused
at the reservation step rather than accepted and rejected later. The predicate inside
`GetTicketsForBookingAsync` is the database-side mirror of the same rule, since a query cannot call
into the domain.

**A lock per seat, taken in a fixed order (Bookings).** Reservation locks
`bookings:reserve:ticket:{id}` rather than one shared key, so reservations for different seats run
concurrently instead of queuing behind each other. Multiple locks are always acquired in ascending
ticket id order, which is what makes overlapping requests deadlock-free: both take seat 7 before seat
9, so neither ends up holding what the other waits for. Duplicate ids are rejected rather than
deduplicated, because the locks are not reentrant.

**Transactional pipeline, scoped to writes (Bookings).** `Bookings.Sql/Pipelines/TransactionBehavior`
is constrained to `ITransactionalRequest`, so it wraps only requests that touch the database — the
registration is open-generic, and without the constraint a Redis-only reservation opened a Postgres
transaction that rolled nothing back. It also stands aside when Wolverine's EF Core middleware already
holds a transaction on the context, which happens on every message-driven path: a second transaction
on that connection is not possible, and committing Wolverine's early would break the outbox guarantee
it exists for.

**After-commit work (Bookings).** Redis does not roll back with a database transaction, so work aimed
at it is queued on `IAfterCommitQueue` and run by `TransactionBehavior` once its own transaction has
committed — on the deferred path it cannot observe the commit, so it logs the work as dropped rather
than guessing. Booking deletes its reservation that way — if the booking then fails, the user still holds the reservation and can retry.
A failure in that cleanup is logged rather than thrown: the commit already happened, so failing the
request would invite a retry of work that is done.

**Payment settled by whichever outcome lands first.** `Booking.Cancel()` refuses a paid booking and
`Booking.MarkPaid()` refuses a cancelled one, so the two contracts need no version to survive
unordered, at-least-once delivery — a late failure cannot void a paid booking, and a late success
cannot claim seats already back on sale. Applying the same outcome twice announces the release once,
so seats are never released a second time after somebody else has taken them.

**Domain event dispatch, two ways.** Bookings uses a `SaveChangesInterceptor`, so persistence and
event emission cannot diverge. Dispatch runs *after* the write, so a handler that changes something
must save that change itself — the surrounding transaction is what keeps its save atomic with the
write that triggered it. Events are cleared before publishing rather than after: a handler that saves
re-enters the interceptor while the aggregate is still tracked, and one still holding its events would
publish them again and re-run that handler, which is recursion rather than a duplicate delivery. Events has no such hook available — Cosmos offers no equivalent — so
dispatch is explicit in the command handler, ordered load → mutate → write → publish. The ordering is
load-bearing in both directions: a refused mutation throws before the write, so nothing is stored
*and* nothing is announced; publishing after the write means no consumer hears about a change that
failed to persist.

**Domain events are translated, never published raw (Events).** The aggregate raises a private
`IDomainEvent`; `Events.Application/IntegrationEvents` maps it to a public contract in
`TicketMaster.Common` and publishes through a single `IIntegrationEventPublisher`. `Events.Domain`
therefore never learns the shared contracts exist. A domain event is allowed to have no public
counterpart — a lineup change has none, because nothing outside depends on who is performing.

**Messages carry resulting state, and a version (Events → Bookings).** `EventRelocated` says which
seats the event *now* has, not which were added or removed, so applying it twice lands in the same
place. Each message also carries the aggregate's `Version`; `Ticket.EventVersion` records how far
each ticket has got and rejects anything not newer, so a redelivered older relocation cannot revert a
newer one. Reconciling a relocation can create tickets, so that handler additionally rejects stale
messages as a whole — a seat that does not exist yet has no version to compare against.

**Transactional outbox storage (Bookings).** `PersistMessagesWithPostgresql` plus
`UseEntityFrameworkCoreTransactions` puts the message store alongside the state it describes, and all
three durability policies are applied, so the broker endpoints are enrolled rather than just the
in-process queues. No test observes it — see [Known gaps](#-known-gaps).

**Persistence-ignorant domain (Events).** `Events.Domain` has *zero* package and project
references — no driver types, no DI abstractions — enforced by architecture tests. Entity ids are
strings; all Cosmos knowledge lives in `Events.Cosmos`.

**Cosmos modelling (Events).** Three containers sharing database-level throughput, each
partitioned by `/id` so reads by id are point reads at ~1 RU. Events embed a *snapshot* of their
venue and performers: renaming a venue deliberately does not rewrite history. Documents are
serialized through a private rehydration constructor, so loading a past event never re-runs the
creation invariants that would reject it.

## 🧪 Testing

Test projects are grouped by service so a module can be lifted out whole when it becomes an
independently deployable microservice:

```
Tests/
├── Bookings/   BookingArchitecture, BookingDomain, BookingIntegration, BookingApi
├── Events/     EventsArchitecture, EventsDomain, EventsApplication, EventsCosmos, EventsApi
└── Users/      UsersArchitecture
```

**Architecture tests** (ArchUnitNET) assert layer dependencies, naming, visibility and layout.
The Events suite additionally forbids any database driver, `System.Drawing`, or DI abstraction from
appearing in `Events.Domain` — the rules that keep the store swappable.

**Unit tests** cover the domain rules of both aggregates and Cosmos document serialization, plus the
Events application handlers against in-memory fake repositories. The serialization tests exercise the
same `JsonSerializerOptions` the `CosmosClient` is built with, so they verify the real document shape
without needing an emulator.

**Integration tests** (`Tests/Bookings/BookingIntegration`) run the real `BookingDomainContext`,
domain event interceptor, transaction behavior and every Bookings command/query handler against real
Postgres and Redis in Testcontainers — one container of each, shared across the project by a single
fixture. They exist for questions a fake cannot answer, and each one settles a claim the design
depends on rather than restating a unit test:

- Redis lock ordering, contention and TTL expiry behave the way the handlers assume
- EF Core tolerates the nested `SaveChangesAsync` that domain event dispatch performs, and clearing
  events before publishing is what stops it recurring
- a second transaction on one context really does throw — which is why the behavior defers instead
- dependency injection genuinely *skips* an open-generic pipeline behavior whose generic constraint
  the request does not satisfy, rather than failing to build it
- after-commit work runs only once the transaction has gone, and not at all when it rolls back

**Needs a running Docker daemon** — every test starts containers; with the daemon down the whole
project fails at fixture initialisation. `Bookings.Sql` and `Bookings.Application` carry
`InternalsVisibleTo("BookingIntegration")` so the tests can construct the internal context,
repositories and handlers.

Handlers are `internal` by architecture rule, so each test project that constructs them relies on an
`InternalsVisibleTo` entry in the production `.csproj`.

```bash
dotnet test Tests/Events/EventsDomain/EventsDomain.csproj
dotnet test Tests/Events/EventsApplication/EventsApplication.csproj
dotnet test Tests/Events/EventsApi/EventsApi.csproj
dotnet test Tests/Events/EventsCosmos/EventsCosmos.csproj
dotnet test Tests/Events/EventsArchitecture/EventsArchitecture.csproj
dotnet test Tests/Bookings/BookingDomain/BookingDomain.csproj
dotnet test Tests/Bookings/BookingIntegration/BookingIntegration.csproj
dotnet test Tests/Bookings/BookingApi/BookingApi.csproj
dotnet test Tests/Bookings/BookingArchitecture/BookingArchitecture.csproj
dotnet test Tests/Users/UsersArchitecture/UsersArchitecture.csproj
```

**Not covered, deliberately:** Wolverine `Consume` handlers need a broker and each is a two-line
delegation to a command that is already covered, so testing them would only prove Wolverine works.
The broker topology, at-least-once redelivery and version-based staleness guards under genuine
out-of-order delivery are therefore untested by design, not by gap. See the `testing` skill's "Not
covered, deliberately" for the rest of that list.

**The suite is green on a clean checkout**, architecture tests included, so a red test means
something actually broke rather than something known. `Bookings.Application` is organised by type then
area — a request under `Commands/` or `Queries/`, its handler under `CommandHandlers/<Area>/` or
`QueryHandlers/<Area>/` — so a handler is never in its command's namespace. `LayoutTest` asserts that a
handler sits under the root its own suffix claims, which is what stops a query handler drifting in
among the command handlers.

## ▶️ Running locally

```bash
dotnet restore TicketMaster.slnx
dotnet build TicketMaster.slnx

dotnet run --project Users.Api/Users.Api.csproj
dotnet run --project Bookings.Api/Bookings.Api.csproj
dotnet run --project Events.Api/Events.Api.csproj
dotnet run --project TicketMaster.ApiGateway/TicketMaster.ApiGateway.csproj
```

Bookings and Users apply EF Core migrations at startup; Events creates its Cosmos database and
containers at startup. Events expects the Cosmos emulator on `https://localhost:8081` — the
emulator's well-known account key is already in `appsettings.Development.json` and is not a secret.

```bash
docker compose up cosmos
```

Central package management is enabled: add package versions to `Directory.Packages.props`, never
`Version="…"` on an individual `<PackageReference>`.

## 🗺️ Known gaps

Being explicit about what is not finished:

- **Events has no outbox.** Everything publishes inline after the Cosmos write, so a crash in between
  loses the message. This matters more now that four contracts flow: a lost `EventCancelled` or
  `EventRelocated` leaves Bookings' tickets permanently disagreeing with the catalogue, not merely
  missing. Wolverine has **no Cosmos message store** (Postgres/SqlServer/Marten only), so the options
  are a hand-rolled outbox document with a publisher loop, or a Postgres purely for messaging. Every
  publish funnels through `IIntegrationEventPublisher`, so it is a change in one place.
  Highest-value fix.
- **Bookings' inbox/outbox enrolment is unverified.** All three durability policies are applied now,
  so RabbitMQ listeners are durable rather than just the in-process queues — but nothing tests it.
  The `BookingIntegration` fixture deliberately never calls `ConfigureRabbitMq`, so proving it needs a
  second fixture with a RabbitMQ container that boots the real host and asserts the listeners came up
  in durable mode. The outbox half is inert either way until something in Bookings publishes.
- **`Events.Application.Pipelines.TransactionBehavior` is a no-op**, and documented as one. Under
  Cosmos there is no honest implementation available: atomicity is confined to a single logical
  partition, and with `/id` partition keys no two documents ever share one.
- **No optimistic concurrency in Events.** `_etag` is neither read nor enforced, so concurrent updates
  are last-write-wins. `Event.Version` is not a substitute — it orders messages for consumers, it does
  not guard the write.
- **The venue and performer delete guards are best-effort.** Each counts upcoming events before
  deleting, but an event can be created in that window and no transaction can span two logical
  partitions. Events are cancelled rather than deleted, so they have no equivalent guard.
- **Nothing pays for a booking.** The endpoints exist and a booking can be made and cancelled, but
  nothing publishes a payment request and no payment service consumes one, so a booking stays `Booked`
  indefinitely and its seats are never released unless the owner cancels it. Bookings has no outbound
  publishing at all today; it is purely a consumer.
- **Nothing tests the gateway.** Its addresses are filled in and the users cluster is ungated — it
  proxies to a service that validates its own tokens — so the system should run end to end on the
  https launch profiles. But there is no gateway test project, so the routing, the introspection call
  and the identity headers are all reasoned rather than observed. It is the only component in the
  solution with no test of any kind.
- **`Booking` has no timestamp.** The list endpoint orders by key descending as a proxy for "newest
  first" and no response can say when a booking was made. Adding `CreatedAt` needs a migration. The
  list also returns a bare array, so "is there more?" is inferred from receiving a full page.
- **`UserId` is a `string` in Bookings and a `long` in Users.** Aligning them means a migration.
- **A relocation can still strand a paid booking.** Cancelling tickets for seats the new venue lacks
  includes already-booked ones, leaving the parent `Booking` pointing at cancelled tickets. Unpaid
  bookings can now be cancelled and their seats released, but `Booking.Cancel()` deliberately refuses a
  paid one — undoing that is a refund, and refunds and notifications are not built.
- **Reservation correctness rests on the distributed locks.** The check and the write both happen with
  every seat's lock held, but the write is not conditional, so a lock lost mid-operation is a real
  double-reservation window rather than a wasted attempt. A deliberate choice, recorded so nobody
  "simplifies" the locking without knowing what it carries.
- **A command that queues after-commit work cannot be sent from a message handler.** The behavior does
  not own that transaction, so it logs the work as dropped instead of running it. Only booking queues
  any, and only over HTTP, so nothing hits this today.
- **The Events Cosmos layer has not been run against a live instance.** Provisioning and the
  repositories are verified by the compiler and by unit-tested serialization, not by execution. The
  cross-partition queries added for the delete guards — an `EXISTS` subquery over `c.performers` and a
  count over `c.venue.id` — have had their shape reviewed and nothing more.
- **`compose.yaml` is stale** — service paths such as `BookingApi/Dockerfile` no longer match the
  project layout. Only the `cosmos` service is currently usable.
- **Users' authentication failures changed status code.** `ErrorType` now drives the response —
  previously every failure was a 400 whatever it said — so a bad login or an invalid refresh token
  answers 401 rather than 400. Login no longer distinguishes "no such user" from "wrong password",
  which closes a user-enumeration hole but is a visible contract change for any existing client.
- **The sale-window rule is still written twice**, because a query cannot call into the domain:
  `Ticket.IsAvailableFor` in the entity and a mirror in `TicketsRepository.GetTicketsForBookingAsync`.
  The grace period itself is no longer duplicated — the query derives its cutoff from
  `Ticket.SaleWindowStart` — but the *shape* of the predicate is, so adding a condition means editing
  both. Closing that would take an `Expression<Func<Ticket, bool>>` on the entity.
- **`POST /api/tickets` is not restricted to admins.** It is the admin repair path, and nothing
  enforces that: no `[Authorize]`, no role, no policy, and it does not read the identity header.
  The gateway requires only an *authenticated* caller for `/bookings-service/**`, so any logged-in
  user can create real, bookable seats. Enforcing it needs a role claim from Users.Api, propagation
  through `AuthTransformProvider`, and a check in `Bookings.Api`.
- **`Events`' `NamingConventionTest` matches by suffix, not by pattern.** Reflected names carry the
  generic arity suffix, so a generic handler reports as ``IdentifiedCommandHandler`2`` and slips the
  check. It passes today only because Events has no generic handlers. The Bookings copy had the same
  trap plus a rule it never called `Check(Architecture)` on; both are fixed there.

## 🗺️ Roadmap

- A durable outbox for Events, and full inbox/outbox enrolment in Bookings, so catalogue changes
  cannot be silently lost
- A payment service, plus the endpoint and outbound publish that would let a booking actually be paid
  for end to end
- Refunds and notifications for a paid booking voided by a relocation or cancellation
- Optimistic concurrency on the Events aggregates via `_etag`
- Integration tests against the Cosmos emulator for Events, as Bookings now has against real Postgres and Redis
- Saga / process-manager work for the full booking flow in Wolverine
- Exception-to-status mapping in Users, as Events and Bookings now have
- A working `compose.yaml` covering every service
