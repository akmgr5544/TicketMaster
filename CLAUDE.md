# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build, Run, Test

Target framework is **.NET 10** (see `global.json` — SDK `10.0.0`, `rollForward: latestMinor`, `allowPrerelease: false`). The solution file is the newer XML format: `TicketMaster.slnx`.

```bash
# Restore / build the whole solution
dotnet restore TicketMaster.slnx
dotnet build TicketMaster.slnx

# Run a specific service (each API is a separate host)
dotnet run --project Bookings.Api/Bookings.Api.csproj
dotnet run --project Events.Api/Events.Api.csproj
dotnet run --project Users.Api/Users.Api.csproj
dotnet run --project TicketMaster.ApiGateway/TicketMaster.ApiGateway.csproj

# Tests — no aggregating test project, run per-project
# Tests are grouped by service under Tests/<Service>/ so a service can be lifted out whole.
dotnet test Tests/Bookings/BookingArchitecture/BookingArchitecture.csproj
dotnet test Tests/Bookings/BookingDomain/BookingDomain.csproj        # Ticket rules, incl. staleness
dotnet test Tests/Bookings/BookingApplication/BookingApplication.csproj  # EventSync consumers
dotnet test Tests/Bookings/BookingIntegration/BookingIntegration.csproj  # interceptor + transactions, SQLite
dotnet test Tests/Events/EventsArchitecture/EventsArchitecture.csproj
dotnet test Tests/Events/EventsDomain/EventsDomain.csproj            # Events domain rules
dotnet test Tests/Events/EventsApplication/EventsApplication.csproj  # handlers, with fake repositories
dotnet test Tests/Events/EventsApi/EventsApi.csproj                  # exception-to-status mapping
dotnet test Tests/Events/EventsCosmos/EventsCosmos.csproj            # Cosmos document serialization
dotnet test Tests/Users/UsersArchitecture/UsersArchitecture.csproj

# Single test
dotnet test Tests/Bookings/BookingArchitecture/BookingArchitecture.csproj --filter "FullyQualifiedName~NamingConventionTest"

# EF Core migrations (services that use Postgres). Run from the API project;
# the DbContext lives in the *.Sql project so `-s` and `-p` differ.
dotnet ef migrations add <Name> -p Bookings.Sql -s Bookings.Api
dotnet ef database update            -p Bookings.Sql -s Bookings.Api
```

Migrations are also applied automatically at startup via `app.ApplyMigrationsAsync()` in `Bookings.Api/Program.cs` and `Users.Api/Program.cs`.

`compose.yaml` exists but references paths that don't match the current project layout (e.g. `BookingApi/Dockerfile`) — it needs updating before it will build. Individual services do have working Dockerfiles.

## Central Configuration

- **Central package management** is on (`ManagePackageVersionsCentrally=true` in `Directory.Packages.props`). Do **not** put `Version="..."` on `<PackageReference>` in individual `.csproj` files — add the version to `Directory.Packages.props` instead. The `Tests/Directory.Packages.props` file imports the root file and adds test-only packages.
- `Directory.Build.props` at the root pins every project to `net10.0`, `Nullable=enable`, `ImplicitUsings=enable`. It also auto-links `.dockerignore` into any project that has a `Dockerfile` — no per-project setup needed.
- `SonarAnalyzer.CSharp` is a `GlobalPackageReference`, so lint warnings from it appear on every build.

## Architecture

Four .NET services plus a shared kernel, wired together at runtime by a YARP API gateway and RabbitMQ:

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
        (Postgres+EF,       (Postgres+EF,             (Cosmos DB,
         JWT issuer)         Wolverine outbox,
                             Redis cache/locks)
                                     │
                                     ▼
                              RabbitMQ (Wolverine)
```

### Per-service layering

**Bookings** and **Events** follow Clean Architecture with the layout:

- `*.Domain` — aggregates, entities, domain events, repository interfaces. Bookings.Domain defines the DDD primitives in `Abstractions/` (`Entity`, `DomainEvent`, `IAggregateRoot`, `IUnitOfWork`).
- `*.Application` — MediatR commands + handlers, application services, integration event handlers, DI wiring (`Extensions/ServiceCollectionExtension.AddApplicationServices`).
- `*.Sql` / `*.Cosmos` — infrastructure: `DbContext` or `EventsCosmosContext`, entity/document mapping, repository implementations, MediatR pipeline behaviors, DI wiring (`AddInfrastructureServices`, plus `ApplyMigrationsAsync` for Bookings and `EnsureContainersAsync` for Events).
- `*.Api` — ASP.NET Core host; `Program.cs` calls `AddInfrastructureServices` then `AddApplicationServices`.

Each project has a marker interface (`IApiAssemblyMarker`, `IApplicationAssemblyMarker`, `IDomainAssemblyMarker`, and `ICosmosAssemblyMarker` in Events.Cosmos — note Bookings.Sql still uses the name `IMongoAssemblyMarker` for historical reasons, despite being Postgres). Architecture tests load assemblies via these markers.

**Users.Api** is a single-project **vertical slice** design (feature folders under `Features/Users/{Authenticate,RefreshToken,Register}`), not the layered layout above. It is the JWT issuer for the system.

### Cross-cutting patterns

- **CQRS via MediatR**: commands live in `*.Application/Commands`, handlers in `CommandHandlers`.
- **Transactional pipeline**: `Bookings.Sql/Pipelines/TransactionBehavior.cs` is registered as an open-generic `IPipelineBehavior<,>` so every MediatR request runs inside a DB transaction (commit on success, rollback + rethrow on exception).
- **Domain event dispatch**: `Bookings.Sql/Interceptors/DomainEventPublisherInterceptor` is a `SaveChangesInterceptor` — domain events are published when the DbContext saves. Wired via `options.AddInterceptors(...)` in `AddInfrastructureServices`.
- **Outbox / messaging**: `Bookings.Application.Extensions.ConfigureRabbitMq` sets up **WolverineFx** with RabbitMQ transport, Postgres-backed outbox (`PersistMessagesWithPostgresql`), EF Core transactions, and `UseDurableLocalQueues`. Uses conventional routing and auto-provisioning.
- **Caching / distributed locks**: Redis via `StackExchange.Redis` + `Medallion.Threading.Redis`. `ICacheService` (`Bookings.Application.Services`) is the abstraction; `IDistributedLockProvider` is registered for cross-instance coordination.
- **Shared integration contracts**: `TicketMaster.Common/IntegrationEvents` — any message crossing service boundaries lives here so producers and consumers share the type.

### API Gateway (`TicketMaster.ApiGateway`)

- YARP with config split across two JSON files loaded at startup: `YarpConfigurations/yarp.clusters.json` (destinations) and `yarp.routes.json` (routing + auth policy). **Destination addresses are currently empty strings** — fill them in for local runs.
- All three service routes require the `GatewayAuthPolicy` (authenticated user).
- Custom auth scheme `UserServiceScheme` (`Handlers/UsersServiceAuthHandler`): the gateway extracts the `Authorization` header from the incoming request, calls `Users.Api` at `api/users/auth?token=...`, and materializes claims (`UserId`, `Email`, `FirstName`, `LastName`, `UserName`) from the response. The `HttpClient` is the named `"UsersService"` client — set its `BaseAddress` before running (currently `""` in `Program.cs`).
- `AuthTransformProvider` runs per-request on any route with an `AuthorizationPolicy` and copies `UserId` / `UserName` claims into `X-Identity-UserId` / `X-Identity-UserName` headers on the proxied request. Downstream services should read identity from those headers, not re-validate the token.

### Tests

- **Layout**: test projects are grouped by service — `Tests/Bookings/`, `Tests/Events/`, `Tests/Users/` — so that everything belonging to one service can be extracted together when a module is split out into its own deployable. Put new test projects under the folder for the service they test.
- **Architecture tests** (`Tests/<Service>/*Architecture`) use **ArchUnitNET.xUnit**. Each project has a `BaseTest` that loads the service's assemblies via marker interfaces into a shared `Architecture` instance; concrete tests assert dependencies, naming, visibility, and colocation rules. Adding a new layer/project means updating `BaseTest.cs` to include its assembly.
- **Unit tests**: `Tests/Events/EventsDomain` covers the Events domain rules; `Tests/Events/EventsCosmos` covers Cosmos document serialization against the real `CosmosJson.Options`, with no emulator needed.
- **Integration tests**: `Tests/Bookings/BookingIntegration` runs the real `BookingDomainContext`,
  domain event interceptor and transaction behavior against SQLite in-memory (`Microsoft.EntityFrameworkCore.Sqlite`,
  a test-only package). It exists for the questions fakes cannot answer — whether EF tolerates the
  nested `SaveChangesAsync` that domain event dispatch performs, and whether dependency injection
  really skips an open-generic pipeline behavior whose generic constraint does not match. It needs
  no running database. `Bookings.Sql` carries `InternalsVisibleTo("BookingIntegration")` so the tests
  can construct the internal context and repositories.
- **The whole test suite is green**, including `BookingArchitecture`. There are no expected failures
  to look past any more, so a red test means something actually broke. `ColocationTest` used to fail by
  design and is gone: Bookings organises `Bookings.Application` by type first — `Commands/` and
  `CommandHandlers/<Area>/` — so a handler is never in its command's namespace. `LayoutTest` guards
  that arrangement instead.
- Handlers are `internal` by architecture rule, so a test project that constructs them needs an
  `InternalsVisibleTo` entry in the production `.csproj` (see `Bookings.Application.csproj`,
  `Events.Application.csproj`).
- Test-only package versions live in `Tests/Directory.Packages.props` (xUnit, Microsoft.NET.Test.Sdk, ArchUnitNET, coverlet). It imports the root `Directory.Packages.props` first, so all versions stay centrally managed.

## Comments

Comment the non-obvious and nothing else. Explain a decision a reader would otherwise undo — why a
value is what it is, why an order matters, why a case is handled that way. Do not write an XML summary
for a record, DTO, marker interface, constructor, or handler whose name and body already say it, and
prefer one line at the point of confusion over a paragraph above the type.

## Conventions worth knowing

- Solution uses `.slnx` (XML) — some older tooling may not read it; prefer commands that take project paths directly.
- `csharp_style_var_*` in `.editorconfig` prefers `var` everywhere; indent is 4 spaces.
- Files with `.DS_Store` are already present throughout the tree — leave them alone in diffs.
- **Don't reorganise folders.** The layout is deliberate; add files to the structure that is there
  rather than restructuring around them. `Bookings.Application` is organised by type then area — see
  the `bookings-service` skill for the layout.
- **A namespace mirrors its folder.** Rider's *namespace does not correspond to file location*
  inspection enforces this and silently restores it, so don't hand-maintain a namespace that differs
  from its path — it will be reverted under you.
- The `Users.Api.csproj` exposes `InternalsVisibleTo("ArchitectureTests")` — internal types are intentionally visible to arch tests.
