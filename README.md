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
| Messaging | WolverineFx over RabbitMQ, with a Postgres-backed outbox in Bookings |
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
         JWT issuer)         Wolverine outbox,         NoSQL API)
                             Redis cache + locks)
                                     │
                                     ▼
                              RabbitMQ (Wolverine)
```

The gateway authenticates every request by calling Users.Api, then forwards the resolved identity
downstream as `X-Identity-UserId` / `X-Identity-UserName` headers — services read identity from
those rather than re-validating the token.

## 📦 Services

| Service | Layout | Store | Responsibility |
|---|---|---|---|
| **Users.Api** | Vertical slice (`Features/Users/…`) | Postgres | Registration, authentication, refresh tokens. Issues the JWTs and answers the gateway's introspection call. |
| **Bookings** | `Domain` / `Application` / `Sql` / `Api` | Postgres + Redis | Reservations and bookings. Owns the ticket lifecycle, with distributed locks guarding concurrent reservation of the same seat. |
| **Events** | `Domain` / `Application` / `Cosmos` / `Api` | Cosmos DB | The catalogue: events, venues, performers. Publishes `EventCreatedIntegrationEvent`, which is what causes tickets to exist in Bookings. |
| **TicketMaster.ApiGateway** | — | — | YARP routing, edge authentication, identity header propagation. |
| **TicketMaster.Common** | — | — | Integration event contracts shared across service boundaries. |

## 🔑 Patterns worth looking at

**CQRS with MediatR.** Commands in `*.Application/Commands`, handlers in `CommandHandlers`, with
open-generic `IPipelineBehavior<,>` for cross-cutting concerns.

**Transactional pipeline (Bookings).** `Bookings.Sql/Pipelines/TransactionBehavior` runs every
MediatR request inside a database transaction — commit on success, rollback and rethrow on failure.

**Domain event dispatch (Bookings).** A `SaveChangesInterceptor` publishes domain events when the
`DbContext` saves, so persistence and event emission cannot diverge.

**Transactional outbox (Bookings).** Wolverine persists messages in Postgres in the same
transaction as the state change, so a crash between "saved" and "published" cannot lose the
message.

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
├── Bookings/   BookingArchitecture, BookingIntegration
├── Events/     EventsArchitecture, EventsCosmos, EventsDomain
└── Users/      UsersArchitecture
```

**Architecture tests** (ArchUnitNET) assert layer dependencies, naming, visibility and colocation.
The Events suite additionally forbids any database driver, `System.Drawing`, or DI abstraction from
appearing in `Events.Domain` — the rules that keep the store swappable.

**Unit tests** cover the Events domain rules and Cosmos document serialization. The serialization
tests exercise the same `JsonSerializerOptions` the `CosmosClient` is built with, so they verify the
real document shape without needing an emulator.

```bash
dotnet test Tests/Events/EventsDomain/EventsDomain.csproj
dotnet test Tests/Events/EventsApplication/EventsApplication.csproj
dotnet test Tests/Events/EventsApi/EventsApi.csproj
dotnet test Tests/Events/EventsCosmos/EventsCosmos.csproj
dotnet test Tests/Events/EventsArchitecture/EventsArchitecture.csproj
dotnet test Tests/Users/UsersArchitecture/UsersArchitecture.csproj
dotnet test Tests/Bookings/BookingArchitecture/BookingArchitecture.csproj
```

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

- **Events has no outbox.** `CreateEventCommandHandler` publishes `EventCreatedIntegrationEvent`
  inline after the write, so a crash in between loses the message and Bookings never creates
  tickets for that event. Bookings has the outbox; Events does not yet. Highest-value fix.
- **`Events.Application.Pipelines.TransactionBehavior` is a no-op**, and documented as one. Under
  Cosmos there is no honest implementation available: atomicity is confined to a single logical
  partition, and with `/id` partition keys no two documents ever share one.
- **Only venues have full CRUD.** Events and performers are still POST-only; reads for them follow
  the same shape as the venue slice.
- **The venue delete guard is best-effort.** It counts upcoming events before deleting, but an
  event can be created in that window and no transaction can span two logical partitions.
- **The Events Cosmos layer has not been run against a live instance.** Provisioning and the
  repositories are verified by the compiler and by unit-tested serialization, not by execution.
- **Gateway destination addresses are empty strings** in `YarpConfigurations/yarp.clusters.json`,
  and the `"UsersService"` client's `BaseAddress` is unset. Fill both in before running the gateway.
- **`compose.yaml` is stale** — service paths such as `BookingApi/Dockerfile` no longer match the
  project layout. Only the `cosmos` service is currently usable.
- **Exception-to-status mapping exists in Events only.** Bookings and Users still surface unhandled
  failures as bare 500s.
- **`Bookings.Sql` still exposes `IMongoAssemblyMarker`**, a leftover name from before it was
  Postgres. Harmless, but confusing.

## 🗺️ Roadmap

- A durable outbox for Events, so ticket creation cannot be silently lost
- CRUD for events and performers, following the venue slice
- Integration tests against the Cosmos emulator and a Postgres container
- Saga / process-manager work for the full booking flow in Wolverine
- Exception-to-status mapping in Bookings and Users, as Events now has
- A working `compose.yaml` covering every service
