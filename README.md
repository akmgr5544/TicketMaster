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
| Messaging | WolverineFx over RabbitMQ (outbox storage configured in Bookings, not yet enrolled — see [Known gaps](#-known-gaps)) |
| Relational store | PostgreSQL via EF Core (Bookings, Users) |
| Document store | Azure Cosmos DB, NoSQL API (Events) |
| Caching / locking | Redis via StackExchange.Redis + Medallion.Threading.Redis |
| Edge | YARP reverse proxy with a custom authentication scheme |
| Testing | xUnit + ArchUnitNET |

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
                                     └───── RabbitMQ ◄──────┘
                                            (Wolverine)

        EventCreated · EventRescheduled · EventRelocated · EventCancelled
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
| **Bookings** | `Domain` / `Application` / `Sql` / `Api` | Postgres + Redis | Reservations and bookings. Owns the ticket lifecycle, with distributed locks guarding concurrent reservation of the same seat. |
| **Events** | `Domain` / `Application` / `Cosmos` / `Api` | Cosmos DB | The catalogue: events, venues, performers, each with full CRUD. Publishes `EventCreated`, `EventRescheduled`, `EventRelocated` and `EventCancelled` — the first is what causes tickets to exist in Bookings, and the rest are what keep them correct. |
| **TicketMaster.ApiGateway** | — | — | YARP routing, edge authentication, identity header propagation. |
| **TicketMaster.Common** | — | — | Integration event contracts shared across service boundaries. |

## 🔑 Patterns worth looking at

**CQRS with MediatR.** Commands in `*.Application/Commands`, handlers in `CommandHandlers`, with
open-generic `IPipelineBehavior<,>` for cross-cutting concerns.

**Transactional pipeline (Bookings).** `Bookings.Sql/Pipelines/TransactionBehavior` runs every
MediatR request inside a database transaction — commit on success, rollback and rethrow on failure.

**Domain event dispatch, two ways.** Bookings uses a `SaveChangesInterceptor`, so persistence and
event emission cannot diverge. Events has no such hook available — Cosmos offers no equivalent — so
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
`UseEntityFrameworkCoreTransactions` puts the message store alongside the state it describes. Note
that only `UseDurableLocalQueues()` is applied today, which covers in-process queues rather than the
RabbitMQ endpoints — so the guarantee is not yet in force. See [Known gaps](#-known-gaps).

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
├── Bookings/   BookingArchitecture, BookingDomain, BookingApplication, BookingIntegration
├── Events/     EventsArchitecture, EventsDomain, EventsApplication, EventsCosmos, EventsApi
└── Users/      UsersArchitecture
```

**Architecture tests** (ArchUnitNET) assert layer dependencies, naming, visibility and colocation.
The Events suite additionally forbids any database driver, `System.Drawing`, or DI abstraction from
appearing in `Events.Domain` — the rules that keep the store swappable.

**Unit tests** cover the domain rules of both aggregates, the application handlers against in-memory
fake repositories, and Cosmos document serialization. The serialization tests exercise the same
`JsonSerializerOptions` the `CosmosClient` is built with, so they verify the real document shape
without needing an emulator.

Handlers are `internal` by architecture rule, so each test project that constructs them relies on an
`InternalsVisibleTo` entry in the production `.csproj`.

```bash
dotnet test Tests/Events/EventsDomain/EventsDomain.csproj
dotnet test Tests/Events/EventsApplication/EventsApplication.csproj
dotnet test Tests/Events/EventsApi/EventsApi.csproj
dotnet test Tests/Events/EventsCosmos/EventsCosmos.csproj
dotnet test Tests/Events/EventsArchitecture/EventsArchitecture.csproj
dotnet test Tests/Bookings/BookingDomain/BookingDomain.csproj
dotnet test Tests/Bookings/BookingApplication/BookingApplication.csproj
dotnet test Tests/Bookings/BookingArchitecture/BookingArchitecture.csproj
dotnet test Tests/Users/UsersArchitecture/UsersArchitecture.csproj
```

**Four `BookingArchitecture` tests fail on a clean checkout.** `ColocationTest` requires a handler to
live in its command's namespace, and the four handlers in `Bookings.Application/CommandHandlers`
predate that rule. Newer slices such as `Bookings.Application/EventSync` colocate and pass. Check the
failing type names before assuming your change caused it.

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
- **Bookings' outbox is configured but not enrolled.** The Postgres message store and EF transaction
  integration are wired up, but only `Policies.UseDurableLocalQueues()` is applied — which covers
  in-process queues, not the RabbitMQ endpoints. `UseDurableInboxOnAllListeners()` /
  `UseDurableOutboxOnAllSendingEndpoints()` are what would make the guarantee real.
- **`Events.Application.Pipelines.TransactionBehavior` is a no-op**, and documented as one. Under
  Cosmos there is no honest implementation available: atomicity is confined to a single logical
  partition, and with `/id` partition keys no two documents ever share one.
- **No optimistic concurrency in Events.** `_etag` is neither read nor enforced, so concurrent updates
  are last-write-wins. `Event.Version` is not a substitute — it orders messages for consumers, it does
  not guard the write.
- **The venue and performer delete guards are best-effort.** Each counts upcoming events before
  deleting, but an event can be created in that window and no transaction can span two logical
  partitions. Events are cancelled rather than deleted, so they have no equivalent guard.
- **A relocation can strand a booking.** Cancelling tickets for seats the new venue lacks includes
  already-booked ones, leaving the parent `Booking` pointing at cancelled tickets. Refunds,
  notifications and booking-level cancellation are not built.
- **The Events Cosmos layer has not been run against a live instance.** Provisioning and the
  repositories are verified by the compiler and by unit-tested serialization, not by execution. The
  cross-partition queries added for the delete guards — an `EXISTS` subquery over `c.performers` and a
  count over `c.venue.id` — have had their shape reviewed and nothing more.
- **Gateway destination addresses are empty strings** in `YarpConfigurations/yarp.clusters.json`,
  and the `"UsersService"` client's `BaseAddress` is unset. Fill both in before running the gateway.
- **`compose.yaml` is stale** — service paths such as `BookingApi/Dockerfile` no longer match the
  project layout. Only the `cosmos` service is currently usable.
- **Exception-to-status mapping exists in Events only.** Bookings and Users still surface unhandled
  failures as bare 500s.
- **`Bookings.Sql` still exposes `IMongoAssemblyMarker`**, a leftover name from before it was
  Postgres. Harmless, but confusing.

## 🗺️ Roadmap

- A durable outbox for Events, and full inbox/outbox enrolment in Bookings, so catalogue changes
  cannot be silently lost
- Booking-level handling when a relocation or cancellation voids tickets someone had already booked
- Optimistic concurrency on the Events aggregates via `_etag`
- Integration tests against the Cosmos emulator and a Postgres container
- Colocating the four legacy Bookings handlers with their commands, so the architecture suite is green
- Saga / process-manager work for the full booking flow in Wolverine
- Exception-to-status mapping in Bookings and Users, as Events now has
- A working `compose.yaml` covering every service
