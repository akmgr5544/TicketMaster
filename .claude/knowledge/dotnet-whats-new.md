# .NET 10 / C# 14 — What TicketMaster Can Use

> Last reviewed: 2026-10-01. The repo targets `net10.0` (`Directory.Build.props`), SDK 10.0.x
> (`global.json`, `rollForward: latestMinor`), C# 14 by default. .NET 11 is preview until
> 2026-11-10 — do not generate `net11.0` or C# 15 code.

## C# 14 features worth using here

### `field` keyword
Validate or normalise in a property without a hand-written backing field. Fits value-object-ish
properties in `Bookings.Domain` / `PaymentSystem/Domain`.

```csharp
public string Name
{
    get;
    set => field = value?.Trim() ?? throw new ArgumentNullException(nameof(value));
}
```

EF Core maps the property normally; the `efcore` skill's backing-field rules still apply to
collections.

### Extension members
Extension properties and static extension members, declared in an `extension` block inside a
static class.

```csharp
public static class HttpContextExtensions
{
    extension(HttpContext context)
    {
        public Guid? CallerId =>
            Guid.TryParse(context.Request.Headers["X-Identity-UserId"], out var id) ? id : null;
    }
}
```

Existing `this`-parameter extension methods (e.g. `CallerIdentity.TryGetUserId`) need no change.

### Null-conditional assignment
`customer?.Order = GetCurrentOrder();` — assigns (and evaluates the right side) only when the
receiver is non-null.

### Also new in C# 14
`nameof` on unbound generics (`nameof(List<>)`), implicit `Span<T>` conversions (can change
overload resolution — see `breaking-changes.md`), modifiers on simple lambda parameters, partial
constructors and events.

## Libraries and runtime

| Feature | Relevance here |
|---|---|
| Built-in OpenAPI (`AddOpenApi` / `MapOpenApi`) | Already used by Users, Bookings, Events. Swashbuckle is not in the repo — keep it that way |
| `TimeProvider` (+ `Microsoft.Extensions.TimeProvider.Testing`'s `FakeTimeProvider`) | Payments uses `TimeProvider` with its own `ControllableTimeProvider`; the other services still call `DateTime.UtcNow` |
| `HybridCache` (GA) | Not used. Not for reservations — ADR-004 |
| `System.Threading.Lock` | Use for any new in-process lock instead of `lock (object)` |
| `FrozenDictionary` / `FrozenSet` | Fits static lookup tables built at startup (e.g. status maps) |
| EF Core 10 | Named query filters, `ExecuteUpdateAsync` taking a plain lambda, `LeftJoin`/`RightJoin` operators |
| Containers | `mcr.microsoft.com/dotnet/aspnet:10.0` / `sdk:10.0` in all five Dockerfiles |

## Versions in this repo vs current ecosystem

Check NuGet before upgrading; these were the pins at last review.

| Package | Repo | Note |
|---|---|---|
| MediatR | 14.1.0 | Commercial/RPL dual license since v13 — ADR-005 |
| WolverineFx (+ RabbitMQ, Postgresql, EF Core, CosmosDb) | 5.41.0 | 6.x exists; it changed defaults and removed APIs. Upgrade with `RestoreV5Defaults()` first, then adopt defaults deliberately. `messaging` skill |
| xunit | 2.9.3 | xUnit v3 (`xunit.v3`) exists; not adopted. Migration touches every fixture's `IAsyncLifetime` |
| Testcontainers | 4.14.0 | Current major |
| Microsoft.Azure.Cosmos | 3.62.1 | — |
| Yarp.ReverseProxy | 2.3.0 | — |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.1 | Keep in step with EF Core 10.0.x |

## .NET 11 watch (GA 2026-11-10, STS)

.NET 10 is LTS; .NET 11 is STS. Staying on 10 until 12 is a reasonable default for this repo.
Previews add C# 15 union types, `closed` hierarchies and collection-expression arguments — useful
for `ErrorType`-style closed sets, but not before the repo moves TFM.
