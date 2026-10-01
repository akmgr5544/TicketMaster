# Common Anti-patterns — TicketMaster

> Mistakes to avoid when writing code here. Each entry says what the repo does today, including
> the deliberate exceptions, so a reviewer does not "fix" a line that is correct by design.

## Generic .NET

### async void
Never, outside event handlers. None in the repo.

### Blocking on async (`.Result`, `.Wait()`, `.GetAwaiter().GetResult()`)
Await all the way.

**Allowed exception:** `OutboxFlushInterceptor.TransactionCommitted` in `Bookings.Sql/Interceptors`
and `PaymentSystem/Shared/Messaging`. It is the *synchronous* EF interceptor override; nothing
commits synchronously today, and if something does the staged messages must still be sent. Leave it.

### `new HttpClient()`
Use `IHttpClientFactory`. The gateway uses the named `"UsersService"` client; Bookings → Events is
gRPC via `Grpc.Net.ClientFactory`. None in the repo.

### `DateTime.Now` / `DateTime.UtcNow` in production code
Inject `TimeProvider` so tests control time.

- **Done right:** PaymentSystem registers `TimeProvider.System`; `AuditTimestampsInterceptor` takes
  it; `PaymentsFixture` swaps in `ControllableTimeProvider`.
- **Known debt** — direct `DateTime.UtcNow`:
  - `Bookings.Application/CommandHandlers/Tickets/ReserveTicketCommandHandler.cs` and
    `.../Bookings/MakeBookingCommandHandler.cs` (`IsAvailableFor(eventId, DateTime.UtcNow)`)
  - `Users.Api/Features/Users/TokenService.cs`, `.../RefreshToken/UserRefreshToken.cs`
  - `Events.Application/CommandHandlers/Delete{Performer,Venue}CommandHandler.cs`
  - `Bookings.Application/Services/Implementations/EventsService.cs` (gRPC deadline — acceptable,
    gRPC's `deadline:` takes a `DateTime`)
- New code: take `TimeProvider`. Do not add more `DateTime.UtcNow` to these services.
- Test polling loops (`while (DateTime.UtcNow < deadline)`) are fine — they measure wall time.

### Catching `System.Exception`
Catch specific types or use an exception filter.

**Allowed:** `Bookings.Sql/Pipelines/TransactionBehavior.cs` catches `Exception` to roll back and
**rethrows**. Filtered catches (`when (exception is HttpRequestException or TaskCanceledException)`
in the gateway auth handler) are the preferred shape.

### Service locator
Constructor injection in handlers and services.

**Allowed:** `GetRequiredService` inside DI factory lambdas in `Extensions/ServiceCollectionExtension.cs`
(interceptor wiring, `OutboxIntegrationEventPublisher`), and in startup scopes
(`ApplyMigrationsAsync`, `EnsureContainersAsync`). That is the composition root.

### Interpolated log messages
`logger.LogWarning("Booking {BookingId} …", id)`, never `$"…"`. None in the repo.

### Undisposed scopes
`using var scope = …CreateScope()` / `await using … CreateAsyncScope()`.

### Tracking queries for reads (EF)
`AsNoTracking()` or project with `Select`. See the `efcore` skill.

### Scoped dependency captured by a singleton
`DbContext`, repositories and `ICacheService` consumers are scoped. A singleton that needs them
takes `IServiceScopeFactory`.

### Dropping `CancellationToken`
Every handler takes one and passes it to every async call (rule 8 in the `cqrs` skill).

## TicketMaster-specific

| Don't | Do | Why |
|---|---|---|
| `SaveChangesAndFlushMessagesAsync()` | Publish through `IIntegrationEventPublisher` | It commits the transaction itself, under `TransactionBehavior`'s feet |
| Re-validate the JWT in Bookings/Events/Payments | Read `X-Identity-UserId` / `X-Identity-UserName` | The gateway already authenticated; downstream trusts its headers |
| Add `GatewayAuthPolicy` to the users route or `payments-webhooks-route` | Leave them ungated | Login has no token yet; PSPs send none and are verified by signature |
| `Version="…"` on a `<PackageReference>` | Add to `Directory.Packages.props` (or `Tests/Directory.Packages.props`) | Central package management |
| `public` handler | `internal sealed` + `InternalsVisibleTo` for the test project | Architecture rule |
| Throw for an expected failure in Users/Payments | Return `Result.Failure(...)` | ADR-002 |
| Return `Result` failure in Bookings/Events | Throw the typed application/domain exception | ADR-002 — Bookings' rollback depends on the throw |
| Assume a multi-document write in Events rolls back | Order writes so a partial failure is safe | Events' `TransactionBehavior` is a no-op over Cosmos |
| Inject `IMediator` | Inject `ISender` | ADR-005 — keeps a mediator swap cheap |
| Put reservations in `HybridCache` / `IMemoryCache` | `ICacheService` (Redis) | ADR-004 — per-instance L1 would double-sell |
| Per-feature folders in PaymentSystem | `Features/<Aggregate>/<Feature>.cs` | Layout rule; `PaymentArchitecture` checks it |
| Hand-maintained namespace that differs from the folder | Namespace = folder path | Rider rewrites it back |
| `EnsureCreated` in a Postgres test fixture | `MigrateAsync` | So migration drift fails tests |
| XML summaries on records, DTOs, obvious handlers, or tests | One line only where a reader would otherwise undo something | CLAUDE.md "Comments" |
