# Knowledge

Cross-service reference for TicketMaster. Service- and technology-specific rules live in
`.claude/skills/*`; this folder holds what spans services.

| File | Read when |
|---|---|
| `common-antipatterns.md` | Writing or reviewing any code — includes the deliberate exceptions not to "fix" |
| `common-infrastructure.md` | Before adding a Result type, error mapping, endpoint wiring, identity read, paging or a transaction behavior — it probably exists |
| `package-recommendations.md` | Adding or upgrading a NuGet package |
| `dotnet-whats-new.md` | Using a C# 14 / .NET 10 feature, or checking repo pins against the ecosystem |
| `breaking-changes.md` | .NET 10 behaviour changes that still bite; the .NET 11 checklist |
| `mediatr-to-mediator-migration.md` | Only when the MediatR → Mediator migration is scheduled |
| `decisions/` | Before reversing a cross-service choice. New ADRs use `decisions/template.md` and the next number |

## Decisions

- [ADR-001](decisions/001-architecture-per-service.md) — architecture is chosen per service
- [ADR-002](decisions/002-error-signalling-per-service.md) — `Result<T>` in slices, exceptions in layered services
- [ADR-003](decisions/003-data-access.md) — EF Core / Cosmos SDK; repositories only for aggregates
- [ADR-004](decisions/004-redis-for-reservations.md) — Redis directly for reservations and locks, not HybridCache
- [ADR-005](decisions/005-stay-on-mediatr.md) — stay on MediatR 14 for now; Mediator is the target

Keep these current in the same change that alters the behaviour they describe.
