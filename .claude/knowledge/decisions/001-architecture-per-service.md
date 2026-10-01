# ADR-001: Architecture Is Chosen Per Service

## Status

Accepted

## Context

TicketMaster is five independently deployable services. They differ in domain complexity, and one
architecture forced on all of them would fit some badly:

| Service | Domain shape |
|---|---|
| Bookings | Rich aggregates (`Booking`, `Ticket`) with invariants, domain events, distributed locks |
| Events | Aggregates (`Event`, `Venue`, `Performer`) over Cosmos, optimistic concurrency, delete guards |
| Users | Thin: register, authenticate, refresh, introspect. Little domain logic |
| PaymentSystem | Rich DDD domain (`PaymentEvent` root, `PaymentOrder`, `Wallet`, double-entry ledger), but features are few and self-contained |
| ApiGateway | No domain. YARP config plus an auth handler |

## Decision

**Each service picks its architecture. Once picked, it is not reorganised.**

- **Bookings, Events: Clean Architecture**, four projects each — `*.Domain`, `*.Application`,
  `*.Sql`/`*.Cosmos`, `*.Api` — with a marker interface per project so ArchUnit can load it.
  `Bookings.Application` is organised by type first (`Commands/`, `CommandHandlers/<Area>/`), so a
  handler never shares its command's namespace. `LayoutTest` guards that.
- **Users.Api: vertical slices**, one project, `Features/Users/<Feature>/`.
- **PaymentSystem: vertical slices on a rich domain**, one project. `Domain/` holds the model,
  `Features/<Aggregate>/<Feature>.cs` holds one file per feature — **no per-feature folder**.
  `PaymentArchitecture` enforces slice isolation and domain purity.
- **ApiGateway: none.** Configuration plus `Handlers/`.

### When to deviate

- A slice that grows real invariants gets a `Domain/` folder in the same project (PaymentSystem
  already did this). It does not become four projects.
- A new service starts as slices unless its domain clearly matches Bookings' or Events' complexity.
- Never restructure an existing service's folders to match another service. See CLAUDE.md.

## Consequences

### Positive

- Bookings and Events get compiler- and ArchUnit-enforced dependency direction where invariants
  are dense.
- Users and Payments add a feature by touching one or two files.
- A service can be lifted out whole; tests are grouped by service under `Tests/<Service>/` for the
  same reason.

### Negative

- Two mental models across the repo. A pattern from one service (e.g. repositories) is wrong in
  another.
- Error signalling differs as a consequence — see ADR-002.

### Mitigations

- Each service has its own skill (`bookings-service`, `events-service`, `users-service`,
  `payments-service`) stating its layout rules. Load it before editing that service.
- Each service has an `*Architecture` test project that fails the build on a layout violation.
