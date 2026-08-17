---
name: cqrs
description: Use when writing or changing a command, query, handler, pipeline behavior, or endpoint dispatch in any TicketMaster service. Covers MediatR (ISender, IRequest, IRequestHandler, IPipelineBehavior) as the current implementation.
---

# CQRS Commands and Handlers

Every write and read goes through a request object and exactly one handler. Cross-cutting concerns
live in the pipeline, never inside handlers.

## Scope

Cross-cutting — applies to every TicketMaster service that dispatches commands and queries
(Users, Bookings, Events). Each service's own skill owns where these types physically live:
Users is vertical slice (`Features/<Area>/<Feature>/`), Bookings and Events are layered
(`*.Application/Commands` + `CommandHandlers`). This skill governs the shape, not the location.

**The rules below are written for the pattern, not the library.** MediatR is today's
implementation and is confined to one section, so replacing it changes that section and the
registration code — not the rules.

## The pattern

| Role | Responsibility |
|---|---|
| **Request** | Immutable data describing one intent. Carries no behavior. |
| **Handler** | Executes exactly one request type. Owns the operation end to end. |
| **Dispatcher** | Routes a request to its handler. Callers depend on this, never on handlers. |
| **Pipeline behavior** | Wraps every request. Home for validation, logging, transactions, metrics. |

## Rules

1. **One handler per request type, one request type per handler.** If a handler needs to serve two
   intents, that's two requests.
2. **Requests are immutable records** named for the intent (`RegisterUser.Command`, not
   `UserRequest`). No settable properties, no mutation inside the handler.
3. **Handlers are `internal sealed`.** Only the request and response types are public. Nothing
   outside the assembly should be able to reference a handler directly.
4. **Signal expected failures — not found, validation failed, conflict — the way the owning service
   does.** The two mechanisms in use are deliberate, not accidental drift:

   | Service | Mechanism |
   |---|---|
   | **Users.Api** | `Result` / `Result<T>` with `Error` and `ErrorType`, in `Users.Api/Shared`. Handlers return failures; they do not throw. |
   | **Bookings, Events** | Exceptions. Handlers throw; an `IExceptionHandler` at the edge maps them to status codes. Events has exactly three types — `EventsDomainException` (broken invariant, 400), `NotFoundException` (404) and `EventsApplicationException` (409) — and adding a failure mode means throwing one of them, not writing a fourth. |

   Do not introduce the Result type into Bookings or Events, and do not throw for expected failures
   in Users. Whichever mechanism a service uses, an expected failure must produce the right status
   code — a "not found" that surfaces as 500 is a bug either way.
5. **A handler never dispatches another request.** Handler-to-handler chaining hides the real
   dependency graph and defeats the pipeline (the inner request re-runs every behavior, including
   transactions). Extract shared logic into a service or static helper and call it from both.
6. **Cross-cutting concerns go in a pipeline behavior.** Validation, logging, transactions,
   metrics, caching. If you find yourself writing the same `try`/`catch` or the same log line in a
   second handler, it belongs in the pipeline.
7. **Endpoints dispatch and map. Nothing else.** No business logic, no `DbContext`, no repository
   in an endpoint — dispatch the request, translate the result to a status code, return.
8. **Every handler takes a `CancellationToken` and passes it down** to every async call it makes.
9. **Depend on the narrowest dispatch abstraction available.** Callers that only send requests take
   the send-only interface, not the full mediator.

## MediatR today

MediatR 14.1.0, registered in each service's `AddApplicationServices` / `AddBusinessServices`.

| Pattern role | MediatR type |
|---|---|
| Request | `IRequest<TResponse>` |
| Handler | `IRequestHandler<TRequest, TResponse>` |
| Dispatcher | `ISender` (use this) / `IMediator` (avoid — wider than needed) |
| Pipeline behavior | `IPipelineBehavior<TRequest, TResponse>` |
| Notification | `INotification` + `INotificationHandler<T>` |

Registration:

```csharp
services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(SomeAssemblyMarker).Assembly));
```

Behaviors are registered as open generics and run in registration order, outermost first:

```csharp
services.AddScoped(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));
```

`Bookings.Sql/Pipelines/TransactionBehavior.cs` is the reference implementation in this repo — it
wraps every request in a database transaction, commits on success, rolls back and rethrows on
exception.

## When MediatR is replaced

Rules 1–9 are implementation-independent and stay as written. What changes:

- the marker interfaces on requests and handlers
- the registration call
- the dispatch abstraction injected into endpoints
- how pipeline behaviors are declared and ordered

Keep the replacement's dispatch abstraction injected at the endpoint boundary. Endpoints that call
handlers directly are the one change that would make the swap expensive, because it spreads the
coupling across every feature.

## Adding a command or query

1. Define the request as an immutable record named for the intent.
2. Define the response record. Return a projection, not an entity.
3. Write the `internal sealed` handler. One operation, `CancellationToken` threaded through.
4. Signal expected failures the way the service does (rule 4): `Result<T>` in Users, a domain
   exception in Bookings and Events.
5. Map the endpoint: dispatch, translate the outcome to a status code, return. No logic.
6. Ask whether anything you wrote is cross-cutting. If so, move it to a behavior.

## Common mistakes

| Symptom | Cause |
|---|---|
| Same validation or logging duplicated across handlers | Belongs in a pipeline behavior |
| Nested transaction, or a behavior running twice for one request | A handler dispatched another request |
| Handler referenced from outside its assembly | Handler isn't `internal` |
| Endpoint has a `DbContext` or business rule in it | Logic that belongs in a handler |
| "Not found" surfaces as a 500 | Nothing maps the failure to a status code — in Events, the exception thrown isn't one of the three the handler knows |
| Cancellation ignored during shutdown or client disconnect | `CancellationToken` not threaded through |
| Entity leaked into an HTTP response | Handler returned the entity instead of a response record |
