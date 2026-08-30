# Bookings Integration Test Migration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move every handler-shaped test in Bookings onto real Postgres and real Redis via Testcontainers, keep domain entities on unit tests, and delete the fake-backed `BookingApplication` project.

**Architecture:** One `BookingsFixture` owns a Postgres container, a Redis container and a single root `ServiceProvider` built by calling the production `AddInfrastructureServices` and `AddApplicationServices`. Every test class joins one xUnit collection, so the suite is serial. Respawn truncates the database and Redis is flushed before each test. Tests dispatch through the real `ISender`, so `TransactionBehavior` and `DomainEventPublisherInterceptor` are the production ones.

**Tech Stack:** .NET 10, xUnit 2.9.3, Testcontainers 4.14.0 (PostgreSql + Redis), Respawn 7.0.0, Npgsql, EF Core 10.

**Spec:** `.claude/skills/testing/SKILL.md`

## Deviation from the writing-plans skill

The skill ends every task with a `git commit` step onto whatever branch is checked out. **This plan commits to an isolated worktree branch only.**

The work happens in the worktree at `.claude/worktrees/bookings-integration-tests` on branch
`worktree-bookings-integration-tests`. Commit each task there — the review packages and the ledger's
crash recovery are built from those commit ranges.

**Never `git push`. Never commit to, switch to, or merge into `main`. Never open a pull request.**
The user reviews `git diff main..worktree-bookings-integration-tests` at the end and decides whether
the branch is squashed onto main, kept, or thrown away.

## Global Constraints

- **Docker must be running** before Task 1. `open -a Docker`, then `docker info` must succeed.
- **Central package management.** Every new package needs a `<PackageVersion>` in `Tests/Directory.Packages.props` and a bare `<PackageReference Include="..."/>` in the csproj. Never a `Version=` attribute on the reference.
- **Namespaces mirror folders.** `Fixtures/BookingsFixture.cs` → `BookingIntegration.Fixtures`. Rider restores this silently; do not hand-maintain a divergent namespace.
- **Do not reorganise folders** beyond what this plan specifies.
- **Comment the non-obvious only.** No XML summary that restates a name.
- **All `DateTime` values written to Postgres must have `Kind == Utc`.** See the hazard note below.
- Target framework `net10.0`, `Nullable=enable`, `ImplicitUsings=enable` — inherited, do not restate in csproj.

## Migration hazards

Read before starting. These are the differences between SQLite/fakes and real Postgres/Redis that will break a naive port.

1. **`DateTimeKind` matters now.** Npgsql maps `DateTime` to `timestamp with time zone` and **throws** on a non-UTC `Kind`. `Ticket.EventDate` is a `DateTime`. Every seeded date must come from `DateTime.UtcNow` arithmetic, never `new DateTime(2026, 1, 1)` (which is `Unspecified`). SQLite accepted those silently. Expect this to be the single most common failure while porting.
2. **Identity columns start at 1 and keep climbing.** Respawn's `ResetAsync` truncates with `RESTART IDENTITY` by default, so ids restart per test — but do not hardcode `1`, `2`, `3`. Capture the ids the seed helper returns.
3. **The fakes let tests invent ticket ids.** Real tests must seed a row first; a ticket id that does not exist behaves differently now (it genuinely is not found).
4. **`FakeAfterCommitQueue` was drained by the test.** The real queue is drained by `TransactionBehavior` after commit. Tests that asserted "queued cleanup" now assert the observable effect: the Redis key is gone after success, and still present after a rollback.
5. **`FakeLockProvider.Acquired` no longer exists.** Lock ordering and release are asserted by observing real contention — hold a lock from a second provider and check the handler's behavior.

---

## File Structure

**Create:**
- `Tests/Bookings/BookingIntegration/Fixtures/BookingsFixture.cs` — containers, DI root, migrations, Respawn, reset
- `Tests/Bookings/BookingIntegration/Fixtures/BookingsCollection.cs` — the single xUnit collection
- `Tests/Bookings/BookingIntegration/Fixtures/IntegrationTest.cs` — base class: reset, act scope, fresh read scope
- `Tests/Bookings/BookingIntegration/Fixtures/Seed.cs` — ticket/booking/reservation arrange helpers
- `Tests/Bookings/BookingIntegration/Handlers/ReserveTicketTests.cs` (22 tests)
- `Tests/Bookings/BookingIntegration/Handlers/MakeBookingTests.cs` (12)
- `Tests/Bookings/BookingIntegration/Handlers/PaymentTests.cs` (11)
- `Tests/Bookings/BookingIntegration/Handlers/EventSyncTests.cs` (11)
- `Tests/Bookings/BookingIntegration/Handlers/CustomerBookingTests.cs` (13)

**Move into `Mechanics/`, ported off SQLite:**
- `TransactionBehaviorTests.cs` (11), `DomainEventDispatchTests.cs` (3), `BookingKeyTests.cs` (2), `TransactionBehaviorRegistrationTests.cs` (3)

**Modify:**
- `Tests/Directory.Packages.props` — add Testcontainers/Respawn, drop Sqlite/SQLitePCLRaw
- `Tests/Bookings/BookingIntegration/BookingIntegration.csproj` — packages, project references
- `Bookings.Application/Bookings.Application.csproj` — retarget `InternalsVisibleTo`
- `CLAUDE.md`, `.claude/skills/testing/SKILL.md`, `.claude/agents/test-engineer.md`

**Delete:**
- `Tests/Bookings/BookingApplication/` entirely (74 tests + 5 fakes)

---

### Task 1: Fixture stands up and talks to both containers

**Files:**
- Modify: `Tests/Directory.Packages.props`
- Modify: `Tests/Bookings/BookingIntegration/BookingIntegration.csproj`
- Create: `Tests/Bookings/BookingIntegration/Fixtures/BookingsFixture.cs`
- Create: `Tests/Bookings/BookingIntegration/Fixtures/BookingsCollection.cs`
- Test: `Tests/Bookings/BookingIntegration/Fixtures/FixtureSmokeTests.cs`

**Interfaces:**
- Produces: `BookingIntegration.Fixtures.BookingsFixture` with `ServiceProvider Services { get; }`, `Task ResetAsync()`; `BookingsCollection.Name` (const string).

- [ ] **Step 1: Confirm Docker is up**

Run: `docker info --format '{{.ServerVersion}}'`
Expected: a version string. If it fails, stop — `open -a Docker` and wait.

- [ ] **Step 2: Add the packages**

In `Tests/Directory.Packages.props`, inside the existing `<ItemGroup>`, add:

```xml
<PackageVersion Include="Testcontainers.PostgreSql" Version="4.14.0" />
<PackageVersion Include="Testcontainers.Redis" Version="4.14.0" />
<PackageVersion Include="Respawn" Version="7.0.0" />
```

Leave the Sqlite entries in place for now — Task 3 removes them, once nothing uses them.

- [ ] **Step 3: Reference them and add the missing project reference**

In `Tests/Bookings/BookingIntegration/BookingIntegration.csproj`, add to the package `ItemGroup`:

```xml
<PackageReference Include="Testcontainers.PostgreSql"/>
<PackageReference Include="Testcontainers.Redis"/>
<PackageReference Include="Respawn"/>
```

And add an explicit reference to the infrastructure project, rather than relying on the transitive one through `Bookings.Application`:

```xml
<ProjectReference Include="..\..\..\Bookings.Sql\Bookings.Sql.csproj" />
```

- [ ] **Step 4: Write the fixture**

Create `Fixtures/BookingsFixture.cs`:

```csharp
using Bookings.Application.Extensions;
using Bookings.Sql;
using Bookings.Sql.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Respawn.Graph;
using StackExchange.Redis;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace BookingIntegration.Fixtures;

public sealed class BookingsFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    private NpgsqlConnection _respawnConnection = null!;
    private Respawner _respawner = null!;

    public ServiceProvider Services { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                // allowAdmin is what lets ResetAsync issue FLUSHDB. Production never needs it, so it
                // is added to the test connection string rather than to AddApplicationServices.
                ["ConnectionStrings:Redis"] = $"{_redis.GetConnectionString()},allowAdmin=true"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();

        // The production wiring, called for real. ConfigureRabbitMq is an IHostBuilder extension and
        // is deliberately not called, which keeps Wolverine and RabbitMQ out without stubbing.
        services.AddInfrastructureServices(configuration);
        services.AddApplicationServices(configuration);

        Services = services.BuildServiceProvider();

        await using (var scope = Services.CreateAsyncScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<BookingDomainContext>();
            await context.Database.MigrateAsync();
        }

        _respawnConnection = new NpgsqlConnection(_postgres.GetConnectionString());
        await _respawnConnection.OpenAsync();

        _respawner = await Respawner.CreateAsync(_respawnConnection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            // Truncating this makes EF believe no migration has been applied, and the next test meets
            // an empty schema.
            TablesToIgnore = [new Table("__EFMigrationsHistory")]
        });
    }

    public async Task ResetAsync()
    {
        await _respawner.ResetAsync(_respawnConnection);

        var multiplexer = Services.GetRequiredService<IConnectionMultiplexer>();

        foreach (var endpoint in multiplexer.GetEndPoints())
        {
            await multiplexer.GetServer(endpoint).FlushDatabaseAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _respawnConnection.DisposeAsync();
        await Services.DisposeAsync();
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }
}
```

- [ ] **Step 5: Write the collection**

Create `Fixtures/BookingsCollection.cs`:

```csharp
namespace BookingIntegration.Fixtures;

// One collection for the whole project. xUnit parallelises across collections, and a single shared
// database cannot survive that.
[CollectionDefinition(Name)]
public sealed class BookingsCollection : ICollectionFixture<BookingsFixture>
{
    public const string Name = "Bookings integration";
}
```

- [ ] **Step 6: Write the failing smoke test**

Create `Fixtures/FixtureSmokeTests.cs`:

```csharp
using Bookings.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace BookingIntegration.Fixtures;

[Collection(BookingsCollection.Name)]
public sealed class FixtureSmokeTests
{
    private readonly BookingsFixture _fixture;

    public FixtureSmokeTests(BookingsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migrations_have_been_applied()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BookingDomainContext>();

        var applied = await context.Database.GetAppliedMigrationsAsync();

        Assert.NotEmpty(applied);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Redis_answers_and_the_reset_clears_it()
    {
        var multiplexer = _fixture.Services.GetRequiredService<IConnectionMultiplexer>();
        var db = multiplexer.GetDatabase();

        await db.StringSetAsync("smoke", "value");
        Assert.Equal("value", await db.StringGetAsync("smoke"));

        await _fixture.ResetAsync();

        Assert.False(await db.KeyExistsAsync("smoke"));
    }

    [Fact]
    public async Task Reset_leaves_the_migration_history_intact()
    {
        await _fixture.ResetAsync();

        await using var scope = _fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BookingDomainContext>();

        // Respawn ignoring __EFMigrationsHistory is what keeps this true; without it the schema is
        // still there but EF reports every migration as pending.
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }
}
```

- [ ] **Step 7: Run and watch it fail for the right reason**

Run: `dotnet test Tests/Bookings/BookingIntegration/BookingIntegration.csproj --filter "FullyQualifiedName~FixtureSmokeTests"`
Expected on first attempt: compile error, or a container pull followed by pass. If it fails on `Bookings.Sql` types being inaccessible, confirm `InternalsVisibleTo("BookingIntegration")` is present in `Bookings.Sql.csproj` (it is) and that Step 3's project reference was added.

- [ ] **Step 8: Make all three pass**

Fix whatever Step 7 surfaced. Do not proceed until all three smoke tests are green.

- [ ] **Step 9: Checkpoint**

Report: the three test results, the image pull time, and the wall-clock for the project. Stop for review. Commit on the branch; never push, never touch `main`.

---

### Task 2: Base class and seed helpers

**Files:**
- Create: `Tests/Bookings/BookingIntegration/Fixtures/IntegrationTest.cs`
- Create: `Tests/Bookings/BookingIntegration/Fixtures/Seed.cs`
- Test: `Tests/Bookings/BookingIntegration/Fixtures/SeedTests.cs`

**Interfaces:**
- Consumes: `BookingsFixture.Services`, `BookingsFixture.ResetAsync()`, `BookingsCollection.Name`.
- Produces:
  - `abstract class IntegrationTest` with `protected ISender Sender`, `protected IServiceProvider Act`, `protected ICacheService Cache`, `protected IDatabase Redis`, `protected Task<T> ReadAsync<T>(Func<BookingDomainContext, Task<T>>)`, `protected Seed Seed`.
  - `sealed class Seed` with:
    - `Task<Ticket[]> TicketsAsync(string eventId, params string[] seats)`
    - `Task<Ticket[]> TicketsAsync(string eventId, DateTime eventDate, long eventVersion, params string[] seats)`
    - `Task<Booking> BookingAsync(string userId, params long[] ticketIds)`
    - `Task ReservationAsync(string userId, string eventId, params long[] ticketIds)`
    - `static DateTime Soon` — a UTC date safely inside the sale window
    - `static DateTime LongPast` — a UTC date outside `Ticket.SaleGracePeriod`

- [ ] **Step 1: Write the base class**

Create `Fixtures/IntegrationTest.cs`:

```csharp
using Bookings.Application.Services.Interfaces;
using Bookings.Sql;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace BookingIntegration.Fixtures;

[Collection(BookingsCollection.Name)]
public abstract class IntegrationTest : IAsyncLifetime
{
    private readonly BookingsFixture _fixture;
    private AsyncServiceScope _act;

    protected IntegrationTest(BookingsFixture fixture)
    {
        _fixture = fixture;
    }

    protected IServiceProvider Act => _act.ServiceProvider;

    protected ISender Sender => Act.GetRequiredService<ISender>();

    protected ICacheService Cache => Act.GetRequiredService<ICacheService>();

    protected IDatabase Redis => Act.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    protected Seed Seed { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        _act = _fixture.Services.CreateAsyncScope();
        Seed = new Seed(_fixture.Services);
    }

    public async Task DisposeAsync()
    {
        await _act.DisposeAsync();
    }

    /// <summary>
    /// Reads through a scope of its own. Asserting through the scope that performed the write returns
    /// the tracked instance and proves nothing about what reached the database — which is the exact
    /// bug the domain event dispatch tests exist to catch.
    /// </summary>
    protected async Task<T> ReadAsync<T>(Func<BookingDomainContext, Task<T>> read)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<BookingDomainContext>());
    }
}
```

- [ ] **Step 2: Write the seed helpers**

Create `Fixtures/Seed.cs`. Note every date is UTC — see hazard 1.

```csharp
using Bookings.Application.Dtos;
using Bookings.Application.Extensions;
using Bookings.Application.Services.Interfaces;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Sql;
using Microsoft.Extensions.DependencyInjection;

namespace BookingIntegration.Fixtures;

/// <summary>
/// Arranges state directly, never through a handler. Arranging by calling the code under test — or
/// another handler — couples the test to behavior it is not trying to prove.
/// </summary>
public sealed class Seed
{
    private readonly IServiceProvider _root;

    public Seed(IServiceProvider root)
    {
        _root = root;
    }

    /// <summary>Inside the sale window, and Utc because Npgsql rejects any other Kind.</summary>
    public static DateTime Soon => DateTime.UtcNow.AddDays(7);

    /// <summary>Outside Ticket.SaleGracePeriod, so the seat is no longer sellable.</summary>
    public static DateTime LongPast => DateTime.UtcNow.AddHours(-6);

    public Task<Ticket[]> TicketsAsync(string eventId, params string[] seats) =>
        TicketsAsync(eventId, Soon, eventVersion: 0, seats);

    public async Task<Ticket[]> TicketsAsync(string eventId,
        DateTime eventDate,
        long eventVersion,
        params string[] seats)
    {
        await using var scope = _root.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BookingDomainContext>();

        var tickets = seats
            .Select(seat => new Ticket(seat, $"venue-for-{eventId}", eventId, eventDate, eventVersion))
            .ToArray();

        context.Tickets.AddRange(tickets);
        await context.SaveChangesAsync();

        return tickets;
    }

    public async Task<Booking> BookingAsync(string userId, params long[] ticketIds)
    {
        await using var scope = _root.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BookingDomainContext>();

        var booking = Booking.Create(userId, BookingStatus.Booked, ticketIds);

        // Create raises BookingCreatedDomainEvent, whose handler books the tickets. Seeding through
        // the context means the interceptor dispatches it, so the seeded state is coherent.
        context.Bookings.Add(booking);
        await context.SaveChangesAsync();

        return booking;
    }

    public async Task ReservationAsync(string userId, string eventId, params long[] ticketIds)
    {
        await using var scope = _root.CreateAsyncScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();

        var entries = ticketIds
            .Select(id => new KeyValuePair<string, ReserveTicketDto>(
                ReservationKeys.Reservation(id),
                new ReserveTicketDto(id, eventId, userId)))
            .ToArray();

        await cache.SetToCacheAsync(entries, TimeSpan.FromMinutes(5));
    }
}
```

- [ ] **Step 3: Add `InternalsVisibleTo` for `ReservationKeys`**

`ReservationKeys` is `internal` in `Bookings.Application`. In `Bookings.Application/Bookings.Application.csproj`, change the existing entry:

```xml
<InternalsVisibleTo Include="BookingApplication" />
```

to:

```xml
<InternalsVisibleTo Include="BookingIntegration" />
<InternalsVisibleTo Include="BookingApplication" />
```

Both are needed until Task 9 deletes `BookingApplication`.

- [ ] **Step 4: Write the failing seed test**

Create `Fixtures/SeedTests.cs`:

```csharp
using Bookings.Application.Dtos;
using Bookings.Application.Extensions;
using Bookings.Application.Services.Interfaces;
using Bookings.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingIntegration.Fixtures;

public sealed class SeedTests : IntegrationTest
{
    public SeedTests(BookingsFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Seeded_tickets_reach_the_database_with_real_keys()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");

        Assert.All(tickets, ticket => Assert.True(ticket.Id > 0));

        var stored = await ReadAsync(context =>
            context.Tickets.OrderBy(t => t.Id).ToArrayAsync());

        Assert.Equal(2, stored.Length);
        Assert.Equal(["A1", "A2"], stored.Select(t => t.Seat));
        Assert.All(stored, ticket => Assert.Equal(TicketStatus.None, ticket.Status));
    }

    [Fact]
    public async Task A_seeded_booking_leaves_its_tickets_booked()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        await Seed.BookingAsync("user-1", tickets[0].Id);

        var stored = await ReadAsync(context =>
            context.Tickets.SingleAsync(t => t.Id == tickets[0].Id));

        Assert.Equal(TicketStatus.Booked, stored.Status);
    }

    [Fact]
    public async Task A_seeded_reservation_is_readable_under_its_namespaced_key()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        await Seed.ReservationAsync("user-1", "evt-1", tickets[0].Id);

        var cache = Act.GetRequiredService<ICacheService>();
        var held = await cache.GetByKeysAsync<ReserveTicketDto>(
            [ReservationKeys.Reservation(tickets[0].Id)]);

        var reservation = Assert.Single(held);
        Assert.Equal("user-1", reservation.UserId);
        Assert.Equal("evt-1", reservation.EventId);
    }

    [Fact]
    public async Task Each_test_starts_from_an_empty_database()
    {
        // Depends on nothing this class seeded. If reset is broken, rows from the tests above survive
        // and this fails — which is the point.
        var count = await ReadAsync(context => context.Tickets.CountAsync());

        Assert.Equal(0, count);
    }
}
```

- [ ] **Step 5: Run to verify it fails**

Run: `dotnet test Tests/Bookings/BookingIntegration/BookingIntegration.csproj --filter "FullyQualifiedName~SeedTests"`
Expected: FAIL — `Seed`/`IntegrationTest` not yet compiling, or `ReservationKeys` inaccessible.

- [ ] **Step 6: Make them pass**

Resolve the failures. If `A_seeded_booking_leaves_its_tickets_booked` fails, the interceptor is not dispatching on the seed path — check that `Seed.BookingAsync` uses the DI-resolved context (which carries the interceptor) and not a hand-built one.

- [ ] **Step 7: Checkpoint**

Report all four results and stop for review. Commit on the branch; never push, never touch `main`.

---

### Task 3: Port the 19 mechanics tests off SQLite

**Files:**
- Move: `TransactionBehaviorTests.cs`, `DomainEventDispatchTests.cs`, `BookingKeyTests.cs`, `TransactionBehaviorRegistrationTests.cs` → `Mechanics/`
- Modify: `Tests/Directory.Packages.props` (remove Sqlite entries)
- Modify: `Tests/Bookings/BookingIntegration/BookingIntegration.csproj` (remove Sqlite references)

**Interfaces:**
- Consumes: `IntegrationTest`, `Seed`, `BookingsFixture`.

- [ ] **Step 1: Move the four files into `Mechanics/` and fix namespaces**

Namespace becomes `BookingIntegration.Mechanics`. Run `dotnet build` and expect it to still compile against SQLite at this point.

- [ ] **Step 2: Convert each class to derive from `IntegrationTest`**

Remove every `SqliteConnection`, `UseSqlite`, hand-built `DbContextOptionsBuilder` and local `InitializeAsync`/`DisposeAsync` that manages them. The base class supplies the scope; `Act.GetRequiredService<BookingDomainContext>()` supplies the context.

`TransactionBehaviorRegistrationTests` is the important one: it currently hand-copies the registration with a comment saying "exactly as `AddInfrastructureServices` makes it." Delete that hand-copy and assert against the fixture's provider, which called the real thing. That is a fidelity gain, not a mechanical port.

- [ ] **Step 3: Run and triage**

Run: `dotnet test Tests/Bookings/BookingIntegration/BookingIntegration.csproj --filter "FullyQualifiedName~Mechanics"`

Two failures are expected and are **not** bugs in the port:

1. Any test seeding a non-UTC `DateTime` — fix with `Seed.Soon` / `Seed.LongPast` (hazard 1).
2. `A_second_transaction_on_one_context_is_not_possible` asserts on an exception message. SQLite and Npgsql word it differently. Run it, read the actual Npgsql message, and assert on that — assert on the exception *type* plus a stable substring, not the full string.

- [ ] **Step 4: Make all 19 pass**

- [ ] **Step 5: Remove SQLite**

From `Tests/Directory.Packages.props` delete:

```xml
<PackageVersion Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.6" />
<PackageVersion Include="SQLitePCLRaw.lib.e_sqlite3" Version="2.1.13" />
<PackageVersion Include="SQLitePCLRaw.bundle_e_sqlite3" Version="2.1.13" />
```

…including the comment above them about the pinned-forward advisory, which no longer applies. From the csproj delete the two matching `<PackageReference>` lines.

- [ ] **Step 6: Verify the removal**

Run: `dotnet build TicketMaster.slnx` then `dotnet test Tests/Bookings/BookingIntegration/BookingIntegration.csproj`
Expected: builds clean, all mechanics + fixture tests pass. Also run `dotnet list TicketMaster.slnx package --vulnerable --include-transitive` and confirm the SQLitePCLRaw advisory is gone from the list.

- [ ] **Step 7: Checkpoint**

Report the 19 results, the Npgsql message you settled on in Step 3, and the vulnerability list diff. Stop for review. Commit on the branch; never push, never touch `main`.

---

### Task 4: ReserveTicket — 22 tests

**Files:**
- Create: `Tests/Bookings/BookingIntegration/Handlers/ReserveTicketTests.cs`
- Delete: `Tests/Bookings/BookingApplication/ReserveTicketCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `IntegrationTest`, `Seed`, `ReserveTicketCommand(string UserId, string EventId, long[] Tickets)`, `ReservationKeys.Reservation(long)`, `ReservationKeys.Lock(long)`.

This is the task with the most fidelity gain and the most rework. Port all 22 names listed below verbatim — they are good names and they describe the right properties.

**Straight ports** (seed tickets, send, assert on Redis or on the thrown exception):
`Reserves_every_ticket_it_was_given`, `Records_who_reserved_the_ticket_and_for_which_event`,
`Refuses_a_ticket_somebody_else_has_already_reserved`, `Reserves_nothing_when_one_of_the_tickets_is_taken`,
`Refuses_a_request_with_no_tickets`, `Refuses_more_tickets_than_allowed`, `Refuses_the_same_ticket_twice`,
`Refuses_a_ticket_that_does_not_exist`, `Refuses_when_only_some_of_the_tickets_exist`,
`Refuses_a_ticket_that_belongs_to_another_event`, `Refuses_a_ticket_that_is_already_sold`,
`Refuses_a_ticket_the_event_has_cancelled`, `Allows_a_ticket_that_was_released_after_a_failed_payment`,
`Reserves_nothing_when_one_of_the_tickets_is_unavailable`

**Rewritten against real infrastructure** — these asserted against `FakeLockProvider` and must now observe real behavior:
`Holds_the_reservation_for_the_configured_time`, `Refuses_when_another_reservation_is_holding_the_ticket`,
`Locks_each_ticket_rather_than_everything_at_once`, `Takes_the_locks_in_ticket_order_not_the_order_it_was_asked_for`,
`Releases_every_lock_it_took`, `Releases_the_locks_it_took_before_hitting_one_it_could_not_have`,
`Releases_its_locks_even_when_the_reservation_fails`, `Releases_its_locks_when_a_ticket_is_unavailable`

- [ ] **Step 1: Write the class and the first straight port**

```csharp
using Bookings.Application.Commands.Tickets;
using Bookings.Application.Dtos;
using Bookings.Application.Exceptions;
using Bookings.Application.Extensions;
using Bookings.Application.Services.Interfaces;
using Bookings.Domain.Exceptions;
using BookingIntegration.Fixtures;
using Medallion.Threading;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace BookingIntegration.Handlers;

public sealed class ReserveTicketTests : IntegrationTest
{
    public ReserveTicketTests(BookingsFixture fixture) : base(fixture) { }

    // Cache and Redis come from IntegrationTest.

    [Fact]
    public async Task Reserves_every_ticket_it_was_given()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        var ids = tickets.Select(t => t.Id).ToArray();

        await Sender.Send(new ReserveTicketCommand("user-1", "evt-1", ids));

        var held = await Cache.GetByKeysAsync<ReserveTicketDto>(
            ids.Select(ReservationKeys.Reservation).ToArray());

        Assert.Equal(2, held.Count);
    }
}
```

- [ ] **Step 2: Run it**

Run: `dotnet test Tests/Bookings/BookingIntegration/BookingIntegration.csproj --filter "FullyQualifiedName~ReserveTicketTests"`
Expected: PASS. If it fails on a missing transaction, note that `ReserveTicketCommand` is deliberately **not** `ITransactionalRequest` — it touches Redis only. That is correct; do not add the marker.

- [ ] **Step 3: Port the remaining 13 straight ports**

One at a time, running after each. For the availability cases, use `Seed.TicketsAsync(eventId, Seed.LongPast, 0, "A1")` for out-of-window, and seed a booking to produce a sold ticket.

- [ ] **Step 4: Rewrite `Holds_the_reservation_for_the_configured_time`**

The fake recorded the `TimeSpan`. Assert the real TTL instead:

```csharp
[Fact]
public async Task Holds_the_reservation_for_the_configured_time()
{
    var tickets = await Seed.TicketsAsync("evt-1", "A1");

    await Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [tickets[0].Id]));

    var ttl = await Redis.KeyTimeToLiveAsync(ReservationKeys.Reservation(tickets[0].Id));

    Assert.NotNull(ttl);
    // Five minutes, allowing for the round trip. Asserting a range rather than equality because the
    // clock moves between the SET and this read.
    Assert.InRange(ttl!.Value, TimeSpan.FromMinutes(4.5), TimeSpan.FromMinutes(5));
}
```

- [ ] **Step 5: Rewrite the seven lock tests against real contention**

The pattern: take the real lock from a second provider, then assert what the handler does. Example for `Refuses_when_another_reservation_is_holding_the_ticket`:

```csharp
[Fact]
public async Task Refuses_when_another_reservation_is_holding_the_ticket()
{
    var tickets = await Seed.TicketsAsync("evt-1", "A1");
    var locks = Act.GetRequiredService<IDistributedLockProvider>();

    // Held by somebody else for the duration of the attempt.
    await using var held = await locks.AcquireLockAsync(ReservationKeys.Lock(tickets[0].Id));

    await Assert.ThrowsAsync<BookingsApplicationException>(() =>
        Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [tickets[0].Id])));
}
```

For `Releases_every_lock_it_took` and the three other release cases, assert the lock is genuinely free afterwards by taking it:

```csharp
[Fact]
public async Task Releases_every_lock_it_took()
{
    var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
    var ids = tickets.Select(t => t.Id).ToArray();

    await Sender.Send(new ReserveTicketCommand("user-1", "evt-1", ids));

    var locks = Act.GetRequiredService<IDistributedLockProvider>();

    foreach (var id in ids)
    {
        // Acquirable immediately means the handler gave it back. A leaked lock strands the seat.
        await using var handle = await locks.TryAcquireLockAsync(
            ReservationKeys.Lock(id), TimeSpan.Zero);

        Assert.NotNull(handle);
    }
}
```

For `Takes_the_locks_in_ticket_order_not_the_order_it_was_asked_for`: hold the **lower** id and send the command with ids in descending order. The handler sorts ascending, so it must block on the held lower id and fail — proving it did not simply take them in the order given. Assert `BookingsApplicationException` and that **no** reservation key was written.

For `Locks_each_ticket_rather_than_everything_at_once`: hold the lock for a ticket in a *different* event and assert an unrelated reservation still succeeds. A constant lock key would serialize them and this would fail.

- [ ] **Step 6: Run the whole class**

Expected: 22 passing.

- [ ] **Step 7: Delete the original**

Delete `Tests/Bookings/BookingApplication/ReserveTicketCommandHandlerTests.cs`. Leave the rest of that project for now.

- [ ] **Step 8: Checkpoint**

Report 22 results and the wall-clock. Call out any test whose meaning changed in the port. Stop for review. Commit on the branch; never push, never touch `main`.

---

### Task 5: MakeBooking — 12 tests

**Files:**
- Create: `Tests/Bookings/BookingIntegration/Handlers/MakeBookingTests.cs`
- Delete: `Tests/Bookings/BookingApplication/MakeBookingCommandHandlerTests.cs`, `BookingCreatedDomainEventHandlerTests.cs`

**Interfaces:**
- Consumes: `MakeBookingCommand(string UserId, string EventId, long[] Tickets) : IRequest<long>`.

Port these 12: `Creates_a_booking_for_the_reserved_tickets`, `Booking_announces_itself_so_its_tickets_can_be_booked`, `Reads_the_reservation_under_its_namespaced_key`, `Refuses_when_only_some_of_the_tickets_are_still_reserved`, `Refuses_when_only_some_of_the_tickets_are_still_available`, `Refuses_a_reservation_belonging_to_somebody_else`, `Refuses_a_reservation_made_for_a_different_event`, `Refuses_more_tickets_than_allowed`, `Refuses_a_request_with_no_tickets`, `Deletes_the_reservation_once_the_transaction_commits`, `Keeps_the_reservation_until_the_transaction_commits`, `Queues_no_cleanup_when_the_booking_is_refused`.

The five from `BookingCreatedDomainEventHandlerTests` are absorbed here rather than ported separately — `Books_every_ticket_the_booking_covers`, `Saves_the_tickets_it_booked` and `Leaves_tickets_the_booking_does_not_cover_alone` are now observable consequences of `MakeBookingCommand`, and `Mechanics/DomainEventDispatchTests` already covers dispatch itself. Keep `Refuses_when_a_ticket_the_booking_covers_has_gone` and `Refuses_when_a_ticket_cannot_be_booked` as direct tests in `Mechanics/DomainEventDispatchTests`.

- [ ] **Step 1: Write the happy path**

```csharp
[Fact]
public async Task Creates_a_booking_for_the_reserved_tickets()
{
    var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
    var ids = tickets.Select(t => t.Id).ToArray();
    await Seed.ReservationAsync("user-1", "evt-1", ids);

    var bookingId = await Sender.Send(new MakeBookingCommand("user-1", "evt-1", ids));

    Assert.True(bookingId > 0);

    var stored = await ReadAsync(context => context.Bookings
        .Include(b => b.BookedTickets)
        .SingleAsync(b => b.Id == bookingId));

    Assert.Equal("user-1", stored.UserId);
    Assert.Equal(BookingStatus.Booked, stored.Status);
    Assert.Equal(ids.Order(), stored.BookedTickets.Select(t => t.TicketId).Order());
}
```

- [ ] **Step 2: Run it, then write the persistence assertion**

```csharp
[Fact]
public async Task Booking_announces_itself_so_its_tickets_can_be_booked()
{
    var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
    var ids = tickets.Select(t => t.Id).ToArray();
    await Seed.ReservationAsync("user-1", "evt-1", ids);

    await Sender.Send(new MakeBookingCommand("user-1", "evt-1", ids));

    // Read through a fresh scope: the interceptor's nested SaveChangesAsync is exactly the thing that
    // could look right in the change tracker and never reach the database.
    var stored = await ReadAsync(context =>
        context.Tickets.Where(t => ids.Contains(t.Id)).ToArrayAsync());

    Assert.All(stored, ticket => Assert.Equal(TicketStatus.Booked, ticket.Status));
}
```

- [ ] **Step 3: The two after-commit tests — the important pair**

```csharp
[Fact]
public async Task Deletes_the_reservation_once_the_transaction_commits()
{
    var tickets = await Seed.TicketsAsync("evt-1", "A1");
    await Seed.ReservationAsync("user-1", "evt-1", tickets[0].Id);

    await Sender.Send(new MakeBookingCommand("user-1", "evt-1", [tickets[0].Id]));

    var held = await Cache.GetByKeysAsync<ReserveTicketDto>(
        [ReservationKeys.Reservation(tickets[0].Id)]);

    Assert.Empty(held);
}

[Fact]
public async Task Keeps_the_reservation_until_the_transaction_commits()
{
    // A booking that fails after the reservation was read must leave the hold in place, so the user
    // can try again. Redis does not roll back with the transaction, which is why the delete is queued
    // on IAfterCommitQueue rather than done in the handler.
    var tickets = await Seed.TicketsAsync("evt-1", "A1");
    await Seed.ReservationAsync("user-1", "evt-1", tickets[0].Id);

    // Book the seat out from under the command so the domain event handler refuses.
    await Seed.BookingAsync("other-user", tickets[0].Id);

    await Assert.ThrowsAnyAsync<Exception>(() =>
        Sender.Send(new MakeBookingCommand("user-1", "evt-1", [tickets[0].Id])));

    var held = await Cache.GetByKeysAsync<ReserveTicketDto>(
        [ReservationKeys.Reservation(tickets[0].Id)]);

    Assert.Single(held);
}
```

If `Keeps_the_reservation_until_the_transaction_commits` cannot be provoked this way — the ticket may be filtered out by `GetTicketsForBookingAsync` before the domain event runs, producing a different exception — that is still a valid arrange. What matters is that the command throws and the key survives. Adjust the arrange, keep the assertion.

- [ ] **Step 4: Port the remaining nine**

- [ ] **Step 5: Run the class, delete both originals**

- [ ] **Step 6: Checkpoint** — report results, stop for review. Commit on the branch; never push, never touch `main`.

---

### Task 6: EventSync — 11 tests

**Files:**
- Create: `Tests/Bookings/BookingIntegration/Handlers/EventSyncTests.cs`
- Delete: `Tests/Bookings/BookingApplication/EventSyncHandlerTests.cs`

**Interfaces:**
- Consumes: the commands in `Bookings.Application/Commands/EventSyncCommands.cs` (namespace `Bookings.Application.Commands`). Read that file for exact record shapes before writing.

Port: `Reschedule_moves_every_ticket_for_the_event`, `Reschedule_leaves_other_events_alone`, `Reschedule_ignores_a_message_that_is_not_newer`, `Cancel_cancels_every_ticket_for_the_event`, `Cancel_applied_twice_is_the_same_as_once`, `Reconcile_cancels_tickets_for_seats_the_new_venue_does_not_have`, `Reconcile_moves_tickets_for_seats_that_survive`, `Reconcile_creates_tickets_for_seats_that_are_new`, `Reconcile_applied_twice_does_not_duplicate_tickets`, `Reconcile_ignores_a_message_that_is_not_newer`, `Reconcile_creates_the_whole_set_when_no_tickets_exist_yet`.

- [ ] **Step 1: Read `EventSyncCommands.cs` and the three handlers** so the command shapes and version semantics are exact.

- [ ] **Step 2: Write `Reschedule_moves_every_ticket_for_the_event`**

Seed with an explicit version so staleness is testable:

```csharp
var tickets = await Seed.TicketsAsync("evt-1", Seed.Soon, eventVersion: 1, "A1", "A2");
```

Assert the new date through `ReadAsync`. Remember the new date must be UTC.

- [ ] **Step 3: Port the rest, one at a time, running after each**

`Reconcile_applied_twice_does_not_duplicate_tickets` is the highest-value one here — it now runs against a real unique-less table, so a genuine duplicate insert shows up as two rows rather than being hidden by a fake's dictionary keyed on id.

For `Reconcile_cancels_tickets_for_seats_the_new_venue_does_not_have`, assert the deliberate two-row outcome documented in the `bookings-service` skill: a seat that leaves and returns gets a fresh ticket, so one cancelled row and one active row for the same seat is correct, not a bug.

- [ ] **Step 4: Run the class, delete the original**

- [ ] **Step 5: Checkpoint** — stop for review. Commit on the branch; never push, never touch `main`.

---

### Task 7: Payments — 11 tests

**Files:**
- Create: `Tests/Bookings/BookingIntegration/Handlers/PaymentTests.cs`
- Delete: `Tests/Bookings/BookingApplication/PaymentHandlerTests.cs`

**Interfaces:**
- Consumes: `ConfirmBookingCommand(long BookingId)`, `ReleaseUnpaidBookingCommand(long BookingId)` from `Bookings.Application.Commands.Payments`.

Port all 11: `Payment_marks_the_booking_paid_and_saves`, `Payment_leaves_the_tickets_booked`, `A_failed_payment_cancels_the_booking`, `A_failed_payment_puts_the_seats_back_on_sale`, `A_failed_payment_does_not_revive_a_ticket_the_event_cancelled`, `The_same_payment_arriving_twice_settles_the_booking_once`, `The_same_failure_arriving_twice_releases_the_seats_once`, `A_failure_arriving_after_payment_leaves_the_booking_paid`, `A_payment_arriving_after_a_failure_leaves_the_booking_cancelled`, `Refuses_to_confirm_a_booking_that_does_not_exist`, `Refuses_to_release_a_booking_that_does_not_exist`.

- [ ] **Step 1: Write the release path first** — it is the one that exercises the domain event

```csharp
[Fact]
public async Task A_failed_payment_puts_the_seats_back_on_sale()
{
    var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
    var ids = tickets.Select(t => t.Id).ToArray();
    var booking = await Seed.BookingAsync("user-1", ids);

    await Sender.Send(new ReleaseUnpaidBookingCommand(booking.Id));

    var stored = await ReadAsync(context =>
        context.Tickets.Where(t => ids.Contains(t.Id)).ToArrayAsync());

    Assert.All(stored, ticket => Assert.Equal(TicketStatus.None, ticket.Status));
}
```

- [ ] **Step 2: The idempotency pair** — send the same command twice and assert one history row

```csharp
[Fact]
public async Task The_same_failure_arriving_twice_releases_the_seats_once()
{
    var tickets = await Seed.TicketsAsync("evt-1", "A1");
    var booking = await Seed.BookingAsync("user-1", tickets[0].Id);

    await Sender.Send(new ReleaseUnpaidBookingCommand(booking.Id));
    await Sender.Send(new ReleaseUnpaidBookingCommand(booking.Id));

    var stored = await ReadAsync(context => context.Bookings
        .Include(b => b.BookingHistories)
        .SingleAsync(b => b.Id == booking.Id));

    Assert.Equal(BookingStatus.Cancelled, stored.Status);
    // Created + Cancelled. A third row means Cancel() raised its event twice, which would release a
    // seat somebody else may have taken by then.
    Assert.Equal(2, stored.BookingHistories.Count);
}
```

- [ ] **Step 3: The two race tests** — send both outcomes and assert first-wins

- [ ] **Step 4: `A_failed_payment_does_not_revive_a_ticket_the_event_cancelled`**

Seed a booking, then cancel one of its tickets through `CancelEventTicketsCommand` (not by mutating the entity), then release the booking. Assert the cancelled ticket stays `Cancelled` and the other returns to `None`.

- [ ] **Step 5: Port the remaining six, run the class, delete the original**

- [ ] **Step 6: Checkpoint** — stop for review. Commit on the branch; never push, never touch `main`.

---

### Task 8: CustomerBookings — 13 tests

**Files:**
- Create: `Tests/Bookings/BookingIntegration/Handlers/CustomerBookingTests.cs`
- Delete: `Tests/Bookings/BookingApplication/CustomerBookingsTests.cs`

**Interfaces:**
- Consumes: `GetBookingQuery(long BookingId, string UserId)`, `ListBookingsQuery(string UserId, int Page, int PageSize)`, `CancelBookingCommand(long BookingId, string UserId)` from `Bookings.Application.Queries`.

Port all 13: `Returns_the_caller_s_own_booking`, `Reports_the_history_of_the_booking`, `Somebody_else_s_booking_is_not_found`, `A_booking_that_does_not_exist_is_not_found`, `Lists_only_the_caller_s_bookings`, `Lists_the_newest_booking_first`, `Pages_through_the_caller_s_bookings`, `Lists_nothing_for_a_caller_with_no_bookings`, `Cancels_the_caller_s_own_booking`, `Cancelling_announces_the_tickets_to_release`, `Refuses_to_cancel_somebody_else_s_booking`, `Refuses_to_cancel_a_booking_that_does_not_exist`, `Refuses_to_cancel_a_booking_that_has_been_paid_for`.

This is the group where real SQL matters most — paging and ordering were previously asserted against an in-memory list.

- [ ] **Step 1: Write the paging test properly**

```csharp
[Fact]
public async Task Pages_through_the_caller_s_bookings()
{
    var made = new List<long>();

    for (var i = 0; i < 5; i++)
    {
        var tickets = await Seed.TicketsAsync($"evt-{i}", "A1");
        var booking = await Seed.BookingAsync("user-1", tickets[0].Id);
        made.Add(booking.Id);
    }

    var first = await Sender.Send(new ListBookingsQuery("user-1", Page: 1, PageSize: 2));
    var second = await Sender.Send(new ListBookingsQuery("user-1", Page: 2, PageSize: 2));
    var third = await Sender.Send(new ListBookingsQuery("user-1", Page: 3, PageSize: 2));

    Assert.Equal(2, first.Length);
    Assert.Equal(2, second.Length);
    Assert.Single(third);

    // Newest first, and no row appears on two pages — the property real OFFSET/LIMIT can break and an
    // in-memory list cannot.
    var returned = first.Concat(second).Concat(third).Select(b => b.Id).ToArray();
    Assert.Equal(made.AsEnumerable().Reverse(), returned);
}
```

- [ ] **Step 2: The scoping tests are the authorization tests** — assert `NotFoundException`, never a 403-shaped outcome

- [ ] **Step 3: Port the remaining 11, run the class, delete the original**

- [ ] **Step 4: Checkpoint** — stop for review. Commit on the branch; never push, never touch `main`.

---

### Task 9: Delete BookingApplication and update the documentation

**Files:**
- Delete: `Tests/Bookings/BookingApplication/` (directory, including the 5 fakes and the csproj)
- Modify: `TicketMaster.slnx`
- Modify: `Bookings.Application/Bookings.Application.csproj`
- Modify: `CLAUDE.md`
- Modify: `.claude/skills/testing/SKILL.md`
- Modify: `.claude/agents/test-engineer.md`

- [ ] **Step 1: Confirm the project is empty of tests**

Run: `find Tests/Bookings/BookingApplication -name '*Tests.cs'`
Expected: no output. If anything remains, it was missed — port it before deleting.

- [ ] **Step 2: Delete the directory and remove it from `TicketMaster.slnx`**

- [ ] **Step 3: Drop the stale `InternalsVisibleTo`**

In `Bookings.Application/Bookings.Application.csproj` remove `<InternalsVisibleTo Include="BookingApplication" />`, leaving only `BookingIntegration`.

- [ ] **Step 4: Update `CLAUDE.md`**

Three changes: remove the `BookingApplication` line from the test command list; rewrite the `BookingIntegration` paragraph, which currently describes SQLite in-memory and its rationale; add Docker to the prerequisites near the build commands.

- [ ] **Step 5: Update `.claude/skills/testing/SKILL.md`**

Delete the `## Status` section — it says "not implemented yet" and instructs its own removal once this lands. Change the layout block's `BookingApplication` line. Verify every other statement is now true of the code.

- [ ] **Step 6: Update `.claude/agents/test-engineer.md`**

This file currently says the opposite of reality in three places: the frontmatter `description` mentions "hand-written fakes, and the SQLite-backed integration tests"; the "What this repository actually uses" table lists Testcontainers under *not present* and SQLite under *in use*; and the "SQLite in-memory is a deliberate choice" paragraph plus the `I Do NOT` line "Replace `BookingIntegration`'s SQLite provider with a container" must go. Replace with a pointer to the `testing` skill and a Docker prerequisite note.

- [ ] **Step 7: Full verification**

```bash
dotnet build TicketMaster.slnx
dotnet test Tests/Bookings/BookingDomain/BookingDomain.csproj
dotnet test Tests/Bookings/BookingIntegration/BookingIntegration.csproj
dotnet test Tests/Bookings/BookingApi/BookingApi.csproj
dotnet test Tests/Bookings/BookingArchitecture/BookingArchitecture.csproj
```

Expected: build clean; 40 + ~93 + 6 + 10 passing; no reference to a deleted project anywhere.

- [ ] **Step 8: Checkpoint**

Report every suite's counts, the total wall-clock for the Bookings suite before and after, and a summary of the documentation changes. Stop for review. Commit on the branch; never push, never touch `main` — hand the user `git diff main..worktree-bookings-integration-tests`.
