---
name: testing
description: Use when writing, moving or restructuring tests in any TicketMaster service — choosing between a unit and an integration test, working with the Testcontainers fixtures in BookingIntegration (Postgres + Redis) or EventsIntegration (Cosmos emulator), seeding data, or adding a new test project. Covers xUnit, Respawn, Testcontainers and the ArchUnit suites.
---

# Testing

Two kinds of test, and the split is by what the code under test *is*, not by how fast the test runs.

| Kind | Covers | Backing |
|---|---|---|
| **Unit** | domain aggregates and entities — invariants, guards, state transitions | nothing; construct the object |
| **Integration** | command and query handlers, and the machinery around them | real Postgres and real Redis in containers |

Everything between those two — a handler's guard clause, a repository method, a pipeline behavior —
is an integration test. There is no third tier of fake-backed handler tests, and reintroducing one is
a regression: see [Why the fakes went away](#why-the-fakes-went-away).

Bookings is the reference for this split. Events now also has a container-backed integration project,
`EventsIntegration`, against the **Cosmos emulator** — see [Events integration](#events-integration).
Its fake-backed `EventsApplication` handler tests still stand and are **not** a regression to remove:
the emulator cannot honour everything (see that section's limits), so the two are complementary, not a
migration in progress. Users is still layered with fakes and no containers; do not "fix" it to match
this document.

## Layout

Test projects are grouped by service so a service can be lifted out whole. Put new projects under the
folder for the service they test — do not reorganise the tree.

```
Tests/Bookings/
  BookingDomain/          unit — Booking, Ticket, Entity. No infrastructure.
  BookingIntegration/     every handler group, on containers
    Fixtures/             the fixture, the collection, the base class, seed helpers
    Mechanics/            EF, DI, transaction behavior, and host startup
    Handlers/             one file per handler area (ReserveTicket, MakeBooking, CreateTicket,
                           EventSync, Payments, CustomerBookings)
  BookingApi/             exception-to-status mapping
  BookingArchitecture/    ArchUnit rules

Tests/Events/
  EventsDomain  EventsApplication  EventsApi  EventsCosmos  EventsArchitecture
  EventsIntegration/      two collections: fast (Cosmos emulator only) + host (adds RabbitMQ)
    Fixtures/             the fast EventsFixture, collection, base class, stub dispatcher, seed
    Repositories/         round-trip and serialization against a live store
    Concurrency/          the _etag / 412 conditional-write path
    DeleteGuards/         the cross-partition delete guards, through ISender
    HostFixtures/         the real Events host (Wolverine + Cosmos outbox) proving the relay
Tests/Users/    UsersApi  UsersArchitecture
Tests/Rpc/      GrpcSeam — the one cross-service test: the Bookings↔Events gRPC error round-trip,
                in-process (TestServer), no containers. See the `rpc` skill.
Tests/Gateway/  GatewayTests — routing, edge auth and identity headers via WebApplicationFactory with
                the introspection client and YARP's forwarder stubbed. In-process, no containers.
```

There is no aggregating test project. Run them per project.

## Running them

**Docker must be running.** Every test in `BookingIntegration` starts containers; with the daemon
down the whole project fails at fixture initialisation, and nothing else reports the cause. There is
no CI, so nobody else will catch this for you.

```bash
dotnet test Tests/Bookings/BookingDomain/BookingDomain.csproj          # fast, no Docker
dotnet test Tests/Bookings/BookingIntegration/BookingIntegration.csproj # needs Docker
dotnet test Tests/Bookings/BookingIntegration/BookingIntegration.csproj \
  --filter "FullyQualifiedName~ReserveTicket"
```

The first run pulls the Postgres and Redis images. Once the images are cached, the whole project —
containers included — runs in well under a minute; measured around ten seconds. It is serial by
design (see below).

## Two fixtures, one project

`BookingsFixture` composes a plain `ServiceProvider` and never calls `ConfigureRabbitMq`. That is what
keeps 113 tests running in about a second, and it should stay that way.

`BookingsHostFixture` is the only place a Wolverine host actually starts. It boots `Program.cs`
unmodified through `WebApplicationFactory<Program>` against its own Postgres, Redis and RabbitMQ
containers, and `Mechanics/HostStartupTests` asserts the host starts and that every application
broker endpoint is `EndpointMode.Durable`.

It exists because a whole class of failure here is invisible to everything else: a durability policy
that was never applied, a handler dependency Wolverine cannot resolve, a code-generation mode with no
compiler behind it. All of them compile, and all of them leave the other suites green. Upgrading
Wolverine to 6.33.0 fails here at startup with *"TypeLoadMode.Dynamic ... no IAssemblyGenerator
(Roslyn) is registered"*, and nowhere else.

Three details carry it:

- **Separate containers, not shared with `BookingsFixture`.** Respawn truncates one database between
  tests; a host pointed at the same one would have its state pulled out from under it.
- **Configuration arrives as environment variables**, not through `WebApplicationFactory`'s hooks.
  `Program.cs` reads every connection string while composing the builder, which is before those hooks
  run; environment variables are already in the default configuration sources by then.
- **`Bookings.Api/Program.cs` ends with `public partial class Program;`** — top-level statements
  generate an internal entry point, and `WebApplicationFactory<T>` needs a public one.

**The two collections run in parallel, and should.** Measured: 7.4s in parallel against 10.6s with
parallelisation disabled, all 116 passing either way — the fixtures own separate containers, so there
is nothing to collide. The one coupling is that the host fixture sets process-global environment
variables; that is safe only because `BookingsFixture` builds its configuration from an explicit
in-memory collection and never reads the environment.

## The fixture

One `BookingsFixture` for the whole project: one Postgres container, one Redis container, one root
`ServiceProvider`, created once and shared.

### Composition is the production path

The fixture builds its service collection by calling the real `AddInfrastructureServices` and
`AddApplicationServices` with the containers' connection strings. It does **not** call
`ConfigureRabbitMq` — that is a separate `IHostBuilder` extension, so Wolverine and RabbitMQ stay out
without anything needing to be stubbed.

This is the point of the whole arrangement: `TransactionBehavior`, `DomainEventPublisherInterceptor`,
the MediatR registration and every DI lifetime are the production ones. A command that forgets
`ITransactionalRequest` runs without a transaction in the test exactly as it would in production, and
a test that asserts rollback will fail.

This is also what caught the real bug: `DomainEventPublisherInterceptor` was registered
`AddSingleton` while it depends on `IPublisher` — a captive dependency. Every domain event handler
resolved its `BookingDomainContext` from the root container instead of the request's scope, so it ran
on a different connection, outside the caller's transaction; a rolled-back booking left its tickets
`Booked` with no booking to explain it. The old SQLite tests hand-built the interceptor with a handler
holding the same context by construction, so they proved the design and never touched the wiring. This
fixture resolves the interceptor the way `Program.cs` does, so the same mistake shows up here.
`Mechanics/DomainEventAtomicityTests.cs` is the test that pins the fix — it is scoped now.

The fixture also builds its provider with `ValidateScopes: true` and `ValidateOnBuild: true`
(`Fixtures/BookingsFixture.cs`). `ValidateScopes` checks every resolution against the root provider at
runtime, which is exactly where the captive-`IPublisher` bug above lived — re-applying the `AddSingleton`
regression turns it back into 37 named failures reading `Cannot resolve
IEnumerable<INotificationHandler<BookingCreatedDomainEvent>> from root provider because it requires
scoped service ITicketsRepository`, instead of a handful of confusing behavioural ones. Don't strip it
as ceremony.

**Never hand-copy a registration into a test.** If a test needs the container wired a particular way,
it calls the production extension method. A hand-rolled `services.AddDbContext<...>` that mirrors
`AddInfrastructureServices` stops mirroring it the day somebody edits one and not the other.

### Three details that carry the risk

1. **Respawn must ignore `__EFMigrationsHistory`.** Truncating it makes EF believe no migration has
   been applied, and the next test meets an empty schema. Configure
   `TablesToIgnore = ["__EFMigrationsHistory"]`, `SchemasToInclude = ["public"]`, `DbAdapter.Postgres`.
2. **`FLUSHDB` needs admin mode**, which `StackExchange.Redis` refuses by default. Append
   `,allowAdmin=true` to the *test* connection string rather than changing `AddApplicationServices` —
   the production wiring stays untouched and the test still exercises it.
3. **Schema comes from `MigrateAsync`, never `EnsureCreated`.** Migrations already run at startup in
   `Program.cs`, so running them here covers them for free and catches a broken migration before
   deployment does.

### Isolation

State is reset between tests. It is **not** rolled back.

**Do not wrap a test in a transaction and roll it back.** `TransactionBehavior` checks
`Database.CurrentTransaction` and defers when one is already open — that is the Wolverine nesting
path. An outer test transaction silently puts every handler onto the deferred branch, so the test
exercises the wrong code path and after-commit work is logged as dropped instead of running. This is
the single easiest way to make this suite quietly worthless.

Instead, before each test: Respawn truncates every table, and Redis is flushed.

All test classes join one xUnit collection, so they run serially. xUnit 2 parallelises across
collections, and a single shared database cannot survive that. If the suite ever gets slow enough to
matter, the fix is a database per test class — not per-test transactions.

## Writing an integration test

### Assert through a fresh scope

Reading back through the same `DbContext` that performed the write returns the tracked instance and
proves nothing about persistence. This is not hypothetical: it is the exact bug
`DomainEventDispatchTests` exists to catch — a domain event handler mutating state that never reaches
the database because dispatch happens after the write.

The base class gives you a scope to act in and a separate one to read back through. Use them.

```
act    → send the command through ISender from the act scope
assert → open a read scope, load the row, assert on what the database actually holds
```

### Arrange by seeding, not by handler

Seed tickets and bookings directly through a context, using the `Seed` helpers. Arranging by calling
the handler under test — or another handler — couples the test to code it is not trying to prove and
turns one failure into several.

The exception is deliberate: a reservation-then-booking test that runs the real reserve handler first
*is* testing the hand-off, and that is the point of it.

### Guard clauses go through the fixture too

The empty-selection, over-limit and duplicate-id rejections run through `ISender` like everything
else, even though they throw before touching infrastructure. A second, fixture-free construction path
saves a few hundred milliseconds and costs a real thing: a guard that starts throwing *after* a write
would still look correct. One path in, one path out.

## What real infrastructure buys

Worth knowing so you assert on the right thing — these are the properties fakes could only simulate.

| Property | Assert against |
|---|---|
| Lock ordering and contention | a real lock held from a second connection; observe the wait and timeout |
| Partial acquisition released | the lock is genuinely free afterwards, not a `Released` list |
| Reservation TTL | the real `TTL` on the key |
| After-commit reservation delete | the key is gone after commit — and **still present** after a rollback |
| `Ticket.Book()` persistence | the `Status` column, read in a fresh scope |
| Rollback and the second-transaction guard | Npgsql's actual behavior, which is what production runs |

## Why the fakes went away

`BookingApplication` used to drive every handler through five hand-written fakes. They were not bad
fakes — but `FakeLockProvider` asserted lock ordering by appending to a `List<string>` that the fake
itself maintained, which is a test of the fake. The properties that matter most in this service — that
two overlapping reservations cannot deadlock, that a partly-acquired set of locks is given back, that a
reservation survives a failed booking — are properties of Redis and Postgres semantics. A fake can
only restate the assumption under test.

Every handler group — ReserveTicket, MakeBooking, EventSync, Payments and CustomerBookings — now runs
against the fixture. `BookingApplication` and its `Fakes/` folder are gone; there is no hand-written
fake for a Bookings handler dependency anywhere in the solution.

If you find yourself adding a fake for `ICacheService`, `IDistributedLockProvider`,
`IBookingRepository`, `ITicketsRepository` or `IAfterCommitQueue` on a Bookings handler test, that is
the signal to use the fixture instead — hand-written fakes for these are a regression, not a
shortcut.

`IEventsService` is the exception — it is a gRPC client to another process, and stubbing it is
correct, because running it for real would test Events rather than Bookings. `StubEventsService` in
`Fixtures/` is that stub; the fixture registers it *after* `AddApplicationServices`, so the
production wiring including the `AddGrpcClient` registration still runs as written and only the last
hop is replaced. The fixture also has to supply `Services:Events:GrpcAddress`, which is never
dialled. See the `rpc` skill.

## Events integration

`EventsIntegration` does for Events what `BookingIntegration` does for Bookings: run the real
repositories, pipeline behaviors and command/query handlers against a **live Cosmos emulator** in
Testcontainers. It exists for the questions unit tests and the serialization suite could not reach —
the conditional-write (`_etag` / 412) path, the cross-partition delete guards, and the aggregate
documents round-tripping through the real SDK and `CosmosJson.Options` together.

### The emulator image is the whole reason this was ever blocked

Pin **`mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-latest`**. It is the only line
with a native **arm64** build (Apple Silicon); the classic `latest` emulator `compose.yaml` pins is
amd64-only and will not start here. The `vnext` emulator is a different beast from the old one:

- It serves **cleartext `http://` on 8081**, not the old self-signed HTTPS. The connection string uses
  `http://`.
- It **rejects the SDK's default Direct mode** — a Direct connection 400s. The client must use
  **`ConnectionMode.Gateway`**, and `LimitToEndpoint = true` (it advertises internal replica addresses
  the client otherwise fails to reach).

The wait strategy keys on the log line `fully ready to accept requests`.

### Composition and the one production seam

The fixture composes the production path — real `AddInfrastructureServices` + `AddApplicationServices`
against in-memory config — exactly as Bookings does, and never calls `ConfigureRabbitMq`. Two
Events-specific points:

- **The connection mode is a config seam, not a hand-built client.** `CosmosOptions.ConnectionMode` is
  `null` in every real deployment (SDK default, Direct); the fixture sets `CosmosConfigs:ConnectionMode
  = Gateway`, and `AddInfrastructureServices` applies it plus `LimitToEndpoint`. This keeps the real
  client construction — including `UseSystemTextJsonSerializerWithOptions = CosmosJson.Options` —
  under test rather than copied into the fixture. It mirrors Bookings appending `allowAdmin` to the
  *test* connection string: touch config, not wiring.
- **`CosmosOutboxDispatcher` is removed, not shadowed.** It depends on `IWolverineRuntime`, which the
  no-broker fixture omits. `services.RemoveAll<IIntegrationEventDispatcher>()` then a
  `StubIntegrationEventDispatcher` — removing rather than registering-after is what lets the provider
  keep **both** `ValidateScopes` and `ValidateOnBuild` on (a leftover dispatcher registration would
  fail `ValidateOnBuild` on the missing runtime). Everything above the dispatcher stays real:
  `OutboxIntegrationEventPublisher` still translates domain events through `IntegrationEventTranslator`.
  This is the direct analogue of `StubEventsService` — replace the one hop to another process, keep
  the wiring that leads to it.

Provisioning goes through the **real** `EnsureContainersAsync`, which grew a `this IServiceProvider`
overload so the fixture and the host share one provisioning path instead of a hand-copy.

### Isolation

Cosmos has no Respawn adapter. The reset analogue in `EventsFixture.ResetAsync`: **delete every
document in each container** (`SELECT VALUE c.id FROM c`, then a point delete per id). Containers, and
their database-level throughput and indexes, stay provisioned from `InitializeAsync`. One xUnit
collection → serial, same reason as Bookings: a single shared database cannot survive cross-collection
parallelism.

### The two-scope trick for a real 412

Each repository owns its own scoped `ETagCache`, so a genuine conflict needs two scopes. The base
class exposes the act scope's repositories (`Events`/`Venues`/`Performers`, one instance per scope, so
its ETag cache persists across calls in a test) and `InScopeAsync` for a fresh scope. A 412 test reads
in the act scope (caching the ETag), mutates the same document from a fresh scope (advancing the stored
ETag), then writes from the act scope — the stale `IfMatch` is a real Cosmos 412 that surfaces as
`ConcurrencyConflictException`. Seeding also uses its own scope for the same reason: it must not prime
the very cache the test depends on being empty.

### Limits — what the emulator cannot prove, so `EventsApplication` still earns its place

- **`MaxItemCount` is not honoured** the way real Cosmos honours it — a `pageSize` of 2 returns all
  matching items in one page. Continuation-token paging therefore cannot be faithfully tested here;
  there is deliberately no paging test, and the manual check against a real account still stands.
- **The outbox relay is proven by the host collection, not the fast one.** The fast fixture stubs the
  Wolverine hop, so *it* says nothing about `CosmosOutboxDispatcher`. The second collection
  (`HostFixtures/`, "Events host") boots the real host on RabbitMQ + the emulator and drives a real
  create-event command; a probe consumer host receiving the relayed message proves the `wolverine`
  container provisions, the relay sends, and envelopes survive the serializer. It mirrors
  `BookingsHostFixture` — its own containers, its own collection, running in parallel with the fast
  one. Not simulated: a resend after a mid-flight crash.
- The `ConcurrencyRetryBehavior` *retry* seam is still covered by `EventsApplication`'s
  `ConflictsBeforeSuccess` fakes, because forcing exactly-N conflicts against live Cosmos is a race.
  This suite proves the conditional write that *produces* the conflict; the fake proves the retry that
  *consumes* it.

## Unit tests

`BookingDomain` covers `Booking`, `Ticket` and `Entity` by constructing them directly. Aggregates
enforce their own invariants and raise their own domain events, so their rules are testable with no
infrastructure at all and belong here — not in the integration project.

Test the rule, not the method. The valuable tests in this repo assert properties: applying a message
twice lands in the same place, a stale version is discarded, a cancelled seat does not return to sale,
a paid booking refuses cancellation.

## Conventions

- **xUnit 2.9.3**, plain `Assert`. No FluentAssertions, no Shouldly.
- **No mocking library.** Moq and NSubstitute are not referenced and should not be added; see above
  for why hand-fakes are also not the answer for handlers.
- **Central package management.** A new test package needs a `<PackageVersion>` in
  `Tests/Directory.Packages.props`, never a `Version=` attribute on the `<PackageReference>`.
- **Naming:** `MethodName_StateUnderTest_ExpectedBehavior`, or a sentence that states the property —
  `A_saved_booking_knows_the_key_the_database_gave_it`. Both styles are in use; match the file.
- **`InternalsVisibleTo` is needed more often than it looks.** Handlers are `internal` by
  architecture rule, and so are `BookingDomainContext`, the repositories and `ReservationKeys` —
  which integration tests need in order to build the cache keys a handler will look for. Entries:
  `Bookings.Application → BookingIntegration`, `Bookings.Sql → BookingIntegration`,
  `Bookings.Api → BookingApi`, `Events.Application → EventsApplication`, `Events.Api → EventsApi`,
  `Users.Api → UsersArchitecture`. Dispatching through `ISender` does not by itself require access to
  a handler — the internals that matter are the keys, the context and the repositories used to seed
  and assert.
- **Architecture suites:** adding a layer or project means updating that suite's `BaseTest.cs` to load
  the assembly via its marker interface.

## Not covered, deliberately

- **Wolverine `Consume` handlers** — they need a broker, and each is a two-line delegation to a
  command that is covered. Testing them would prove Wolverine works.
- **The HTTP layer** — `BookingApi` covers exception-to-status mapping. `Microsoft.AspNetCore.Mvc.Testing`
  *is* referenced and used — `BookingsHostFixture` boots the host through `WebApplicationFactory<Program>`
  — but only to prove startup and durability, not to send requests: there is no HTTP request/response
  test suite exercising controllers over the wire.
- **`IdentifiedCommandHandler`** — inert; `IRequestManager` has no implementation.

## Known gaps

Reviewed and accepted for now — not a to-do list, but a reader should know these before trusting a
green run more than it has earned, or before "fixing" one and making things worse.

**Test-quality gaps, reviewed and accepted for now:**
- `Handlers/EventSyncTests.Cancel_applied_twice_is_the_same_as_once` cannot distinguish guarded from
  unguarded behaviour. `Ticket.Cancel` is idempotent by construction rather than by its `IsStale`
  guard, so removing the guard produces the same end state. Inherited unchanged from the fake-based
  original.
- `Ticket.Cancel`'s and `Ticket.Relocate`'s own `IsStale` guards are covered, and verified by
  mutation: `BookingDomain.TicketTests.Ignores_a_cancellation_that_is_not_newer` and
  `Ignores_a_relocation_that_is_not_newer` both fail when the guard is removed from the method they
  name.
- Empirical nuance worth recording, NOT a correction: the message-level staleness guard in
  `ReconcileEventVenueCommandHandler` is redundant with that handler's `Except(covered)` filter for
  the specific case of redelivering a message whose seats already exist un-cancelled. It is NOT
  redundant for the case rule 2 of the `bookings-service` skill actually describes — a stale message
  naming a seat with no existing ticket — which was verified to regress when the guard was removed.
  Do not change rule 2.
- Switching the context registration to `AddDbContextPool`, `AddDbContextFactory`, or
  giving `AddDbContext` an `optionsLifetime: Singleton` would hand the options-factory lambda the root
  provider and silently reopen the exact hole `Mechanics/DomainEventAtomicityTests` exists to close —
  don't make that change without re-deriving why the interceptor needs the request's scope.
- `Handlers/ReserveTicketTests.Takes_the_locks_in_ticket_order_not_the_order_it_was_asked_for` polls
  with a 200ms deadline inside the handler's 250ms `LockWaitTimeout`. That ~50ms margin is the only
  thing keeping the test sensitive to a regression. Under a slow CI runner it degrades toward a false
  *pass*, not a false failure — a starved poll only starts checking after a buggy handler has already
  timed out and released the lock on its own.
- `Mechanics/DomainEventDispatchTests.Publishes_the_creation_event_exactly_once` wraps
  `SaveChangesAsync` in `Record.ExceptionAsync`, which swallows any exception, not only the recursive
  re-book it is guarding against. An unrelated EF or connection error would be silently absorbed and
  the test would pass or fail on the publish counter alone.
- `Handlers/ReserveTicketTests.Releases_the_locks_it_took_before_hitting_one_it_could_not_have` proves
  the lock is free *afterwards*, not that the handler ever held it and gave it back. The name reads as
  a stronger claim than the assertion makes.
- The sale-window rule now has one statement and one evaluator: `Ticket.IsAvailableFor`. Both handlers
  read their tickets by id and apply it in memory, so there is no database-side predicate left to drift
  — `GetTicketsForBookingAsync` is gone and `MakeBookingCommandHandler` calls `GetTicketsByIdAsync`.
  Covered at both levels: `BookingDomain.TicketTests.Availability_ends_a_while_after_the_event_starts`
  for the rule itself, and
  `BookingIntegration.Handlers.MakeBookingTests.Refuses_a_ticket_whose_event_is_past_the_sale_window`
  (using `Seed.LongPast`) for the booking path, verified by mutation: dropping the
  `!ticket.IsAvailableFor(...)` clause from the handler's guard turns the latter red while the other
  thirteen `MakeBooking` tests stay green.
- `Mechanics/DomainEventAtomicityTests.The_handler_saves_through_the_requests_context` asserts scope
  identity via `context.ChangeTracker.Entries<Ticket>().Count() == 1` — a white-box proxy for "this is
  the same context the handler used." Switching the repository's read to `AsNoTracking` would fail this
  test with no real defect behind it. Accepted as the cheapest way to observe a property that has no
  black-box signature.

**Latent, not observed today:**
- Dependency injection cannot see through the `AddDbContext` options-factory lambda. If
  `DomainEventPublisherInterceptor` ever grows a dependency that itself needs `BookingDomainContext`,
  the result is unbounded recursion during context construction, not the clean circular-dependency
  exception DI normally throws. Nothing depends on the interceptor that way today.

## Comments

Comment the non-obvious and nothing else. A test class earns a summary when it explains *why* it
exists — which bug it pins down, which property it protects. A test whose name already says what it
does needs no summary.
