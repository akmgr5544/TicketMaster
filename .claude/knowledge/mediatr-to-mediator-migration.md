# MediatR to Mediator Migration — TicketMaster Touch Points

> Not scheduled. ADR-005 records why the repo stays on MediatR 14.1.0 for now. This is the plan for
> when it moves to [Mediator](https://github.com/martinothamar/Mediator) (MIT, source-generated).
> Check Mediator's README for the current major's exact API before starting — the details below
> are for 3.x.

## What changes in the API

| Concept | MediatR | Mediator |
|---|---|---|
| Namespace | `using MediatR;` | `using Mediator;` |
| Handler return | `Task<TResponse>` | `ValueTask<TResponse>` |
| Void request | `IRequest` / `IRequestHandler<T>` returning `Task` | `IRequest` / handler returning `ValueTask<Unit>` |
| Behavior `next` | `RequestHandlerDelegate<TResponse>`, `await next()` | `MessageHandlerDelegate<TRequest, TResponse>`, `await next(request, ct)` |
| Notification handler | `Task Handle(T, ct)` | `ValueTask Handle(T, ct)` |
| Registration | `AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...))` | `AddMediator(...)` — handlers found at compile time |
| Dispatch | `ISender`, `IPublisher` | same names |

## Where it lands in this repo

| Area | Files |
|---|---|
| Package refs | `Directory.Packages.props` (drop `MediatR`, add `Mediator.Abstractions` + `Mediator.SourceGenerator`); csprojs: `Bookings.Domain`, `Bookings.Application`, `Events.Application`, `Users.Api`, `PaymentSystem` |
| Registration | each service's `Extensions/ServiceCollectionExtension.cs` (`AddApplicationServices` / `AddBusinessServices` / `AddInfrastructureServices`) |
| Pipeline behaviors | `Bookings.Sql/Pipelines/TransactionBehavior.cs`; `Events.Application/Pipelines/` `ConcurrencyRetryBehavior` (outermost) then a no-op `TransactionBehavior`; `PaymentSystem/Shared/Pipelines/TransactionBehavior.cs` |
| Domain events | `Bookings.Domain/Abstractions/DomainEvent.cs`, `PaymentSystem/Domain/Abstractions/DomainEvent.cs` (`: INotification`); the `DomainEventPublisherInterceptor` in each that publishes them |
| Notification handlers | `Bookings.Application/DomainEventHandlers/*`, `PaymentSystem/Features/PaymentOrders/{Settle,Fail}.cs` |
| Test-only behavior | `Tests/Bookings/BookingIntegration/Fixtures/BookingsFixture.cs` registers `AfterHandlerFailureBehavior<,>` |

## Repo-specific risks

1. **Constrained open-generic behaviors.** Bookings' and Payments' `TransactionBehavior`s use
   `where TRequest : notnull, ITransactionalRequest`, and rely on MS DI *skipping* the behavior for
   requests without the marker. `BookingIntegration` has a test proving that. Confirm Mediator
   honours the constraint the same way, or convert the check to a runtime `if (request is not
   ITransactionalRequest) return await next(request, ct);`. Keep that test green either way.
2. **Behavior ordering.** Events' `ConcurrencyRetryBehavior` must stay outermost so a retry
   re-runs the whole handler. Re-check the order after switching registration.
3. **Internal handlers across projects.** Handlers are `internal` by architecture rule. The source
   generator must run in the assembly that contains them (`Bookings.Application`,
   `Events.Application`, `Users.Api`, `PaymentSystem`). Bookings' `TransactionBehavior` lives in
   `Bookings.Sql` — check how the generator picks up behaviors from another assembly.
4. **Nested publish inside `SaveChangesAsync`.** Domain events are published from an interceptor
   during save, and their handlers may save again. `BookingIntegration` covers this; it must stay
   green.
5. **`ValueTask` awaited once.** No behavior here caches or re-awaits `next`'s result today; keep
   it that way.
6. **Architecture tests.** ArchUnit rules that name `MediatR` types (handler visibility, naming)
   need their type references updated.

## Order of work

1. One service at a time, Users first (smallest, no domain events), then Payments, Events, Bookings.
2. Per service: swap packages → namespaces → return types → behaviors → registration → build.
3. Run that service's Architecture, Domain/Application and Integration test projects (see
   CLAUDE.md for the list) before moving to the next.
4. Update the `cqrs` skill's "MediatR today" section and ADR-005's status.
