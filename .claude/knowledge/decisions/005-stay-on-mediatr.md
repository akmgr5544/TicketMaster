# ADR-005: Stay on MediatR 14 for Now; Mediator Is the Migration Target

## Status

Accepted — revisit before a production deployment

## Context

Every service with handlers uses MediatR 14.1.0 (`Bookings.Domain`, `Bookings.Application`,
`Events.Application`, `Users.Api`, `PaymentSystem`). Since v13 MediatR is dual-licensed by Lucky
Penny Software: RPL-1.5 or a paid commercial license. No license key is configured anywhere in the
repo, so MediatR logs a license warning at startup.

The alternative is `Mediator` (martinothamar, MIT, source-generated). Wolverine is already in
Bookings and Payments, but replacing MediatR with Wolverine's in-process bus would merge two
concerns the code keeps separate (domain events vs integration messages).

## Decision

**Keep MediatR while the project is non-commercial. Before anything ships commercially, either buy
a license or migrate to `Mediator` following `../mediatr-to-mediator-migration.md`.**

Until then, keep the swap cheap:

- Endpoints and controllers depend on `ISender`, never `IMediator`.
- Handlers are `internal sealed`.
- Cross-cutting work goes in `IPipelineBehavior<,>`, not in handlers.
- Domain events are `INotification` (`Bookings.Domain`, `PaymentSystem/Domain/Abstractions/DomainEvent`)
  dispatched by an EF interceptor — one place to change.

### When to deviate

- Do not add MediatR features without a Mediator equivalent (e.g. `IMediator` facade, runtime
  handler registration by reflection).
- Do not adopt Wolverine as the in-process mediator.

## Consequences

### Positive

- No migration cost now.
- The constraints above are the same rules the `cqrs` skill already requires.

### Negative

- Licensing is unresolved; the startup warning stays.
- MediatR is reflection-based; startup and AOT are not a concern here, so this costs nothing yet.

### Mitigations

- `mediatr-to-mediator-migration.md` lists every touch point in this repo.
