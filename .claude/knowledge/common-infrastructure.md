# Common Infrastructure — Where It Already Lives

> The shared building blocks in this repo. Reuse or copy these; do not paste in a generic version
> or add a library for something that already exists here.

## Result and error mapping (slice services)

| Piece | Users.Api | PaymentSystem |
|---|---|---|
| `Result`, `Result<T>` | `Shared/Result.cs`, `Shared/ResultT.cs` | `Shared/Results/Result.cs`, `ResultT.cs` |
| `Error(Code, ErrorType, Message)` | `Shared/Error.cs` | `Shared/Results/Error.cs` |
| `ErrorType` (`NotFound`, `BadRequest`, `Unauthorized`, `Forbidden`) | `Shared/ErrorType.cs` | `Shared/Results/ErrorType.cs` |
| `error.ToProblem()` → ProblemDetails with `code` extension | `Shared/ErrorResults.cs` | `Shared/Results/ErrorResults.cs` |

A new slice service copies the PaymentSystem set. See ADR-002.

## Exception mapping (layered services)

`Bookings.Api/Handlers/BookingsExceptionHandler.cs` and `Events.Api/Handlers/EventsExceptionHandler.cs`
— `internal sealed` `IExceptionHandler`s that map typed exceptions to status codes through
`IProblemDetailsService`. Add a new exception type to the handler's `Map` switch, or it is a 500.

Every API registers `AddProblemDetails()` and calls `UseExceptionHandler()`.

## Endpoints

- **Users.Api, PaymentSystem — minimal APIs, auto-discovered.** A slice implements
  `IEndpointMarker` (`void MapEndpoint(IEndpointRouteBuilder)`); Scrutor registers every
  implementation and startup maps them. Never hand-register an endpoint in `Program.cs`.
  - `Users.Api/Shared/IEndpointMarker.cs`, `PaymentSystem/Shared/Endpoints/IEndpointMarker.cs`
- **Bookings.Api, Events.Api — controllers.** Bookings controllers derive from
  `Bookings.Api/Abstractions/BaseController.cs`.

## Caller identity

The gateway sets `X-Identity-UserId` / `X-Identity-UserName`, overwriting anything the client sent.
Read it, never a route or body value:

- PaymentSystem: `httpContext.TryGetUserId(out var userId)` — `Shared/Endpoints/CallerIdentity.cs`
- Bookings: `BaseController` reads the header.

## Transactions and pipelines

- Bookings (`Bookings.Sql/Pipelines`) and PaymentSystem (`Shared/Pipelines`): `TransactionBehavior<,>`
  constrained to `ITransactionalRequest`. Mark a request with it to get a DB transaction; reads
  don't need it.
- Events (`Events.Application/Pipelines`): `ConcurrencyRetryBehavior<,>` (retries the handler on `ConcurrencyConflictException`, i.e. a Cosmos 412)
  then a `TransactionBehavior<,>` that is **a deliberate no-op** — Cosmos with `/id` partition keys
  cannot make two writes atomic. A handler writing two documents must not assume rollback.

## Integration events and outbox

- Contracts: `TicketMaster.Common/IntegrationEvents` — any cross-service message lives here.
- Publishing: `IIntegrationEventPublisher` → `OutboxIntegrationEventPublisher` stages into
  Wolverine's `DbContextOutbox` on the open transaction; `OutboxFlushInterceptor` sends after commit.
  Bookings (`Bookings.Sql`) and PaymentSystem (`Shared/Messaging`). Events uses a Cosmos outbox.
- Details: `messaging` skill.

## Time

`TimeProvider` registered as `TimeProvider.System` in PaymentSystem; tests swap in
`Tests/Payments/PaymentIntegration/Fixtures/ControllableTimeProvider.cs`. Copy that pattern into
other services as their `DateTime.UtcNow` debt is paid down (see `common-antipatterns.md`).

## Pagination

- Bookings (Postgres): offset paging — `ListBookingsQuery(UserId, Page, PageSize)` returns
  `Bookings.Application/Dtos/PagedResult<T>(Items, Page, PageSize, Total)`.
- Events (Cosmos): continuation tokens — `List{Events,Venues,Performers}Query(PageSize, ContinuationToken)`.
  Do not use `OFFSET` against Cosmos; it costs RUs for every skipped item.

## Validation

No FluentValidation. Request records are validated in the handler or domain, and failures go
through the service's normal error path (ADR-002). Add a library only with a reason it beats that.

## Startup helpers

`ApplyMigrationsAsync()` (Bookings, Users, Payments) and `EnsureContainersAsync()` (Events) in each
service's `Extensions/ServiceCollectionExtension.cs`.
