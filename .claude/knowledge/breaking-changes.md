# Breaking Changes — What Still Matters for TicketMaster

> Last reviewed: 2026-10-01. The repo is already on .NET 10, so the 9 → 10 migration is done. This
> file keeps only the .NET 10 behaviour changes that can still bite new code here, and tracks
> .NET 11. Full lists: [.NET 10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10),
> [EF Core 10](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes),
> [.NET 11](https://learn.microsoft.com/en-us/dotnet/core/compatibility/11).

## .NET 10 changes relevant to this repo

| Change | Where it can bite | What to do |
|---|---|---|
| `PackageReference` without `Version` errors (`NU1015`) unless CPM is on | Any project outside the root `Directory.Packages.props` tree | CPM is on; keep every project under it. Versions go in `Directory.Packages.props` / `Tests/Directory.Packages.props` |
| `dotnet restore` audits transitive packages | Restore can surface new NuGet audit warnings | Fix by pinning the transitive package in `Directory.Packages.props`, not by disabling audit |
| `.slnx` is the default solution format | Tooling that only reads `.sln` | Pass project paths to commands (CLAUDE.md) |
| `WithOpenApi()` deprecated (`ASPDEPR002`) | New endpoints in Users / Payments | Use `AddOpenApi()` + endpoint metadata (`WithName`, `Produces`) |
| Cookie auth no longer redirects API endpoints | Not used — auth is the gateway's custom scheme + JWT | — |
| `BackgroundService` runs all of `ExecuteAsync` on the thread pool | Any `BackgroundService` you add (none in the repo; Wolverine hosts its own) | Synchronous work before the first `await` no longer blocks startup; don't rely on it running first |
| Configuration preserves `null` values | Options binding where a key is present but null | Check the value, not key existence — the gateway's `Services:Users:BaseAddress` `?? throw` already does |
| C# 14 implicit `Span` conversions change overload choice | Calls with both array and span overloads | Rare here; watch for it in string/byte helpers in `PaymentProvider` (webhook signing) |
| `System.Linq.AsyncEnumerable` in the BCL | Adding `System.Linq.Async` package | Don't — it now causes ambiguous calls |
| System.Text.Json rejects duplicate serialized property names | Cosmos documents (`CosmosJson.Options`), integration events | Covered by `Tests/Events/EventsCosmos`; add a serialization test for any new document type |
| EF Core 10: `ExecuteUpdateAsync` takes a plain lambda | Bulk updates | Write setters imperatively; don't build expression trees |
| EF Core 10: complex type column names are uniquified/full-path | Adding `ComplexProperty` mappings | Set `HasColumnName` explicitly; check the generated migration |

Not relevant here: Blazor, SQLite, SQL Server/Azure SQL JSON type, EF's SqlClient `Application
Name` injection (repo is Npgsql), multi-targeted EF tooling (every project is single-target).

## .NET 10 → 11 (GA 2026-11-10, STS)

Not planned — see `dotnet-whats-new.md`. If the repo ever moves:

1. `Directory.Build.props` TFM and `global.json` SDK — both in one change.
2. All `Microsoft.*` and `Npgsql.EntityFrameworkCore.PostgreSQL` to 11.x together.
3. Check WolverineFx, MediatR, ArchUnitNET and Testcontainers support the new TFM first.
4. Review C# 15's [compiler breaking changes](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/breaking-changes)
   (`union`, `closed` are contextual keywords).
5. Rebuild containers — .NET 11 base images are restructured and smaller.
6. Run every test project listed in CLAUDE.md, including the Docker-backed integration suites.
