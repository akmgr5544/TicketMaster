# ADR-002: Expected Failures — Result in Slices, Exceptions in Layered Services

## Status

Accepted

## Context

Handlers need one way to report expected failures (not found, forbidden, invalid state). The repo
has two, and both are deliberate. They split along the architecture line from ADR-001.

## Decision

### Users.Api and PaymentSystem: `Result<T>`

Handlers return `Result` / `Result<T>`. The types are project-owned and nearly identical in both
services:

- `Users.Api/Shared/{Result,ResultT,Error,ErrorType,ErrorResults}.cs`
- `PaymentSystem/Shared/Results/{Result,ResultT,Error,ErrorType,ErrorResults}.cs`

```csharp
public record Error(string Code, ErrorType Type, string Message);
public enum ErrorType { NotFound, BadRequest, Unauthorized, Forbidden }
```

The endpoint maps a failure with `error.ToProblem()`. `ErrorResults` is the **only** place
`ErrorType` becomes a status code. An endpoint returning `Results.BadRequest(...)` directly made
every failure a 400 before, which is why that file exists.

### Bookings and Events: domain/application exceptions

Handlers throw typed exceptions (`NotFoundException` derives from `BookingsApplicationException` /
`EventsApplicationException`, plus domain exceptions). One `IExceptionHandler` per API maps them:
`Bookings.Api/Handlers/BookingsExceptionHandler.cs`, `Events.Api/Handlers/EventsExceptionHandler.cs`.
Anything not in its map is a 500.

In Bookings, exceptions are also what makes `TransactionBehavior` roll back; a `Result` failure
would commit unless the behavior learned to inspect it. Events has no rollback to protect (its
`TransactionBehavior` is a no-op over Cosmos), so there the choice is consistency with Bookings and
the shared layered structure.

### Unexpected failures, everywhere

They stay exceptions. Every API calls `AddProblemDetails()` and `UseExceptionHandler()`.

### When to deviate

- A new slice-style service uses `Result<T>`. Copy the five files from PaymentSystem; do not add a
  library (`ErrorOr`, `FluentResults`).
- Do not introduce `Result<T>` into Bookings or Events piecemeal. Converting one handler gives a
  service two conventions, and in Bookings breaks the rollback guarantee for that handler.
- A new `ErrorType` value means a new arm in `ErrorResults.StatusFor` and `TitleFor`.

## Consequences

### Positive

- Slices: failure is in the signature; tests assert on `IsSuccess` and `Error.Type`.
- Bookings: rollback on failure is automatic and cannot be forgotten.

### Negative

- Two conventions. Code moved between services must be converted.
- In Bookings/Events, an exception type missing from the handler's map surfaces as a 500.

### Mitigations

- `Tests/Users/UsersApi` and `Tests/Events/EventsApi` / `Tests/Bookings/BookingApi` pin the
  error-to-status mapping.
- The `cqrs` skill states which convention each service uses (its rule 4).
