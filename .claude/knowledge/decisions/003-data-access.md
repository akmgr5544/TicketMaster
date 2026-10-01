# ADR-003: Data Access — EF Core on Postgres, Cosmos SDK for Events, Repositories Only for Aggregates

## Status

Accepted

## Context

Four services own data. Generic .NET guidance says "use `DbContext` directly, the repository
pattern over EF is an anti-pattern". That is right for the slice services and wrong for the
layered ones, where the domain project must not reference EF or Cosmos.

## Decision

| Service | Store | Access |
|---|---|---|
| Users.Api | Postgres, EF Core (`UsersDomainContext`) | `DbContext` directly in the slice handler |
| PaymentSystem | Postgres, EF Core (`PaymentDbContext`) | `DbContext` directly in the slice handler |
| Bookings | Postgres, EF Core (`BookingDomainContext` in `Bookings.Sql`) | Aggregate repositories (`IBookingRepository`, `ITicketsRepository`, both `: IUnitOfWork`) defined in `Bookings.Domain`, implemented in `Bookings.Sql` |
| Events | Cosmos DB, `Microsoft.Azure.Cosmos` SDK (`EventsCosmosContext`) | Aggregate repositories defined in `Events.Domain`, implemented in `Events.Cosmos` |

Repositories here exist for dependency direction, not abstraction for its own sake: they are per
aggregate, return aggregates, and are the only way the Application layer reaches storage.

### Conventions on every EF Core service

1. `AsNoTracking()` or a `Select` projection for reads.
2. `CancellationToken` passed to every async EF call.
3. Entity configuration in `IEntityTypeConfiguration<T>` classes.
4. Cross-cutting work in interceptors: `DomainEventPublisherInterceptor` (Bookings, Payments),
   `AggregateVersionInterceptor` and `AuditTimestampsInterceptor` (Payments),
   `OutboxFlushInterceptor` (Bookings, Payments).
5. Migrations applied at startup by `ApplyMigrationsAsync()`. Payments' test fixtures use
   `MigrateAsync`, never `EnsureCreated`, so model drift fails tests.
6. Audit timestamps from `TimeProvider` — Payments does this; Bookings and Users do not yet (see
   `common-antipatterns.md`).

### When to deviate

- No Dapper or raw SQL today. Reach for `FromSql` / Dapper only for a reporting query EF cannot
  express well, and keep it in the infrastructure project (`*.Sql`) for layered services.
- Do not add a generic `IRepository<T>`. A new repository is per aggregate and lives in `*.Domain`.
- Do not add repositories to Users or Payments.

## Consequences

### Positive

- `Bookings.Domain` / `Events.Domain` stay free of persistence references, which the architecture
  tests enforce.
- Slice services keep the shortest path from endpoint to query.

### Negative

- Two styles; see ADR-001.
- Query handlers go through the repository too (`ListBookingsQueryHandler` → `IBookingRepository`),
  so repository interfaces accumulate read shapes. Keep paging and projection inside the
  `Bookings.Sql` implementation so the query stays a single `AsNoTracking` round trip.

### Mitigations

- `efcore` skill for EF rules, `document-db` skill for Cosmos rules, service skills for layout.
