---
name: efcore
description: Use when writing EF Core queries, saving changes, configuring entities, adding migrations, or touching a DbContext in any TicketMaster service backed by Postgres (Users.Api, Bookings). Covers change tracking, Add vs AddAsync, query-then-Update, AsNoTracking, projections, column sizing, and backing fields.
---

# EF Core

EF Core 10 with `Npgsql.EntityFrameworkCore.PostgreSQL`. Most EF bugs in this repo come from the
change tracker doing something different from what the code appears to say.

## Scope

Cross-cutting — every TicketMaster service that persists to Postgres. Events.Api uses MongoDB and
is out of scope. Each service's skill owns where its `DbContext` and configurations live.

## Writes

1. **Use `Add`, not `AddAsync`.** Per the EF docs: *"The only value generator that does this and
   ships with EF Core is `HiLoValueGenerator<TValue>`. Using this generator is uncommon; it is
   never configured by default. This means that the vast majority of applications should use `Add`,
   and not `AddAsync`."* Nothing here uses HiLo. `AddAsync` just adds an awaited `ValueTask` that
   never yields.

2. **Never query an entity and then call `Update` on it.** The docs are explicit: *"each of the
   approaches use either a query or a call to one of `Update` or `Attach`, but **never both**."*
   A queried entity is already tracked — mutating it is enough, and `SaveChangesAsync` writes only
   the changed columns. Calling `Update` on it marks **every** column modified, so the UPDATE
   rewrites every field and blows away the per-property change detection.

   ```csharp
   // Wrong — rewrites every column
   var user = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
   user.RefreshToken = token;
   db.Users.Update(user);
   await db.SaveChangesAsync(ct);

   // Right — writes only RefreshToken (and anything else actually changed)
   var user = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
   user.RefreshToken = token;
   await db.SaveChangesAsync(ct);
   ```

   `Update` is for **disconnected** entities — ones built from a request payload that were never
   queried.

3. **One `SaveChangesAsync` per operation.** It is already a transaction across everything tracked.
   Multiple saves in one handler create multiple transactions and a partially-applied failure mode.

4. **Enforce uniqueness with the database, not a prior read.** `FirstOrDefaultAsync(...)` followed
   by `Add` is a time-of-check/time-of-use race: two concurrent requests both see nothing and both
   insert. The unique index then turns the loser into a `DbUpdateException` — an unhandled 500
   where the user should have got a 400. Either catch `DbUpdateException` and map it to a domain
   error, or keep the pre-check for the friendly message *and* handle the exception.

5. **Pass `CancellationToken` to every async EF call** — `FirstOrDefaultAsync`, `ToListAsync`,
   `SaveChangesAsync`, `AnyAsync`.

6. **Never run parallel operations on one `DbContext`.** Per the docs: *"Entity Framework Core does
   not support multiple parallel operations being run on the same DbContext instance."* Always
   await each call immediately.

## Reads

7. **`AsNoTracking` for every query whose result you will not modify.** Tracking costs a snapshot
   per entity and per property. If the handler returns data and saves nothing, it should not track.

8. **Project to the shape you need — don't load entities to read two columns.**

   ```csharp
   // Wrong — loads and tracks every column
   var user = await db.Users.FirstOrDefaultAsync(x => x.Id == id, ct);
   return new UserDto(user.Id, user.Email);

   // Right — SELECT only what's needed, no tracking
   return await db.Users
       .Where(x => x.Id == id)
       .Select(x => new UserDto(x.Id, x.Email))
       .FirstOrDefaultAsync(ct);
   ```

   A projection with `Select` is not tracked, so `AsNoTracking` is redundant on it.

9. **Use `AnyAsync` for existence checks**, not `FirstOrDefaultAsync(...) != null` or `CountAsync`.

10. **Keep predicates translatable and index-aware.** Anything EF can't translate to SQL either
    throws or silently evaluates client-side over the full table. Be aware that an `OR` across two
    separately-indexed columns often can't use either index — if that query gets hot, split it.

11. **Always paginate collection queries.** No unbounded `ToListAsync()` on a table that grows.

12. **`AsSplitQuery` when including multiple collection navigations**, to avoid the cartesian
    explosion of a single joined result set.

## Modeling

13. **Size columns to what actually gets stored, and verify it.** A `HasMaxLength` smaller than the
    real value is a runtime failure on Postgres (`value too long for type character varying(n)`),
    not a compile error. Measure before choosing a number — e.g. `PasswordHasher<T>` emits exactly
    **84** characters for the v3 format regardless of password length (61 bytes Base64-encoded:
    1 marker + 4 PRF + 4 iterations + 4 salt-length + 16 salt + 32 subkey).

14. **Take `DbContextOptions<TContext>`, not the non-generic `DbContextOptions`.** The non-generic
    form compiles but breaks as soon as a second `DbContext` is registered in the same container,
    because both resolve the same options object.

15. **One `IEntityTypeConfiguration<T>` per entity**, applied in `OnModelCreating`. Prefer
    `ApplyConfigurationsFromAssembly` over hand-listing each configuration — a hand-written list
    silently omits any configuration someone forgets to add.

16. **Beware side-effecting property setters.** By default *"the backing field, if one is found by
    convention or has been specified, is used when new objects are constructed, typically when
    entities are queried from the database. Properties are used for all other accesses."* So a
    setter with a side effect does **not** fire when EF materializes a row — which is usually what
    you want, but it means correctness depends on EF discovering the backing field. If it ever
    doesn't, the side effect fires on every read from the database. Don't put logic that matters in
    a setter; if you must, state the dependency in a comment and configure the access mode
    explicitly rather than relying on convention.

17. **Don't map secrets in plaintext.** Refresh tokens, API keys, and similar should be stored
    hashed, so a database leak isn't directly replayable.

## Migrations

18. **The `DbContext` and the startup project differ**, so both flags are required:
    ```bash
    dotnet ef migrations add <Name> -p Bookings.Sql -s Bookings.Api
    dotnet ef database update    -p Bookings.Sql -s Bookings.Api
    ```
    For Users.Api the `DbContext` lives in the same project, so `-p`/`-s` are both `Users.Api`.

19. **Read the generated migration before committing it.** A shortened column, a dropped index, or
    an unintended `NOT NULL` on existing data all look identical to a harmless diff until they run.

20. **`ApplyMigrationsAsync()` at startup is a single-instance convenience.** Two instances booting
    together race on the same migration. Fine for local development; not a deployment strategy.

## Quick reference

| Doing | Use |
|---|---|
| Insert a new entity | `Add` (never `AddAsync` — no HiLo here) |
| Change something you queried | Mutate it, then `SaveChangesAsync`. No `Update` |
| Save something built from a request payload | `Update` / `Attach` — never with a prior query |
| Read data you won't modify | `Select` projection, or `AsNoTracking` |
| Check existence | `AnyAsync` |
| Enforce uniqueness | Unique index + handle `DbUpdateException` |

## Common mistakes

| Symptom | Cause |
|---|---|
| `value too long for type character varying(n)` | `HasMaxLength` smaller than the real value (rule 13) |
| UPDATE rewrites every column | Query-then-`Update` (rule 2) |
| Duplicate insert returns 500 instead of 400 | Check-then-insert race, `DbUpdateException` unhandled (rule 4) |
| Query slow, memory high on a read-only path | Tracking a full entity instead of projecting (rules 7, 8) |
| Options resolve to the wrong context | Non-generic `DbContextOptions` (rule 14) |
| Entity state changes unexpectedly on load | Side-effecting setter and backing-field discovery (rule 16) |
| `A second operation started on this context` | Parallel use of one `DbContext` (rule 6) |

## Sources

Verified against EF Core 10 docs, retrieved 2026-08-08.

- Change tracking: https://learn.microsoft.com/ef/core/change-tracking/
- `Add` vs `AddAsync`: https://learn.microsoft.com/ef/core/change-tracking/miscellaneous
- Explicit tracking: https://learn.microsoft.com/ef/core/change-tracking/explicit-tracking
- Identity resolution: https://learn.microsoft.com/ef/core/change-tracking/identity-resolution
- Disconnected entities: https://learn.microsoft.com/ef/core/saving/disconnected-entities
- Backing fields: https://learn.microsoft.com/ef/core/modeling/backing-field
