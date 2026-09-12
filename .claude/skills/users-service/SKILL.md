---
name: users-service
description: Use when working on Users.Api — authentication, registration, refresh tokens, JWT issuing, user profile, feature slices under Features/Users, or the introspection endpoint the gateway calls.
---

# Users Service

Users.Api owns identity for the whole system: it is the sole JWT issuer and the only service that
stores credentials. The gateway calls it on every authenticated request.

## Scope

Covers `Users.Api/` only.

**This service is vertical slice. It is not layered.** Bookings and Events use Clean Architecture
with separate Domain/Application/Sql projects; Users deliberately does not. Do not import that
layering here, and do not apply these slice rules there.

**Load alongside this skill:**
- `cqrs` — before writing any command, handler, or endpoint dispatch.
- `efcore` — before writing any query, save, entity configuration, or migration.

Those two hold the rules that apply across every service. This skill holds what is specific to
Users. See `api-gateway` for the introspection contract this service must serve.

## Slice anatomy

One feature per folder under `Features/<Area>/<Feature>/`, one file per feature, containing both
the operation and its endpoint:

```csharp
public static class RegisterUser                    // named for the intent
{
    public sealed record Command(...) : IRequest<Result<Response>>;
    public sealed record Response(...);

    internal sealed class Handler : IRequestHandler<Command, Result<Response>>
    {
        // one operation, start to finish
    }
}

public sealed class RegistrationEndpoints : IEndpointMarker   // outside the static class
{
    public void MapEndpoint(IEndpointRouteBuilder endpoints) { ... }
}
```

Code shared by several features in one area sits one level up, at `Features/<Area>/`
(`Features/Users/TokenService.cs`). Code shared across areas goes in `Shared/`.

## File map

| Path | Owns |
|---|---|
| `Features/<Area>/<Feature>/` | One feature: command, response, handler, endpoint |
| `Features/<Area>/*.cs` | Helpers shared within that area |
| `Shared/` | `Result`, `Result<T>`, `Error`, `ErrorType`, `IEndpointMarker` |
| `Database/` | `UsersDomainContext`, `Configurations/`, `Migrations/` |
| `Entities/` | Persistence entities |
| `Options/` | Bound configuration records |
| `Extensions/ServiceCollectionExtension.cs` | DI registration and the migration helper |
| `Program.cs` | Composition and pipeline |

The live feature slices under `Features/Users/` are `Authenticate`, `Register`, `RefreshToken`,
`Introspect` and `SetRole`. `Introspect` (`Introspect/IntrospectUser.cs`) is the gateway-facing
contract: `IntrospectUser.Query(Guid UserId)` maps `GET api/users/auth` with `.RequireAuthorization()`
and returns `{ id, email, firstName, lastName, userName, role, permissions }` — see rule 11 and the
`api-gateway` skill. `SetRole` (`SetRole/SetUserRole.cs`) maps `PUT api/users/{id:guid}/role` behind
the `AdminOnly` policy for promoting/demoting users.

## User identity is a Guid

`User.Id` is a `System.Guid`, minted in the entity constructor with `Guid.CreateVersion7()` — v7 is
time-ordered, so app-assigned inserts stay index-friendly rather than scattering like a random GUID.
It is stored as a native Postgres `uuid` (`ValueGeneratedNever`, since the app assigns it), and EF
rehydrates through a private parameterless constructor so a load never re-runs the public constructor
and mints a throwaway id. The migrations were squashed to a single clean `InitialCreate` on the uuid id.

**The store keeps a `Guid`; the wire carries the string form.** The JWT `NameIdentifier` (subject) is
`Id.ToString()`, and `Introspect` parses the subject back with `Guid.TryParse` before dispatching
`IntrospectUser.Query(Guid)`. Bookings likewise keys on a `Guid` and parses the gateway's string
`X-Identity-UserId` header at its edge.

## Rules

1. **A feature never reaches into another feature.** No handler references another feature's
   `Command`, `Response`, or `Handler`. Shared logic moves up to the area or to `Shared/` — that
   promotion is the signal the code is genuinely shared.
2. **Endpoints are discovered, not hand-registered.** Implement `IEndpointMarker`; Scrutor picks it
   up from the assembly. Never add a `MapPost` in `Program.cs`.
3. **Handlers return `Result<T>` and do not throw for expected failures.** Endpoints translate the
   result; they never inspect business state themselves.
4. **`Error` fields carry what their names say** — `Code` is a stable machine-readable identifier,
   `Message` is the human-readable text. Do not put the sentence in `Code` and leave `Message`
   empty.
5. **`ErrorType` determines the status code**, and `ErrorResults.ToProblem` in `Shared/` is the one
   place that reads it: `NotFound` → 404, `BadRequest` → 400, `Unauthorized` → 401, `Forbidden` →
   403, each as a `ProblemDetails` carrying the `Code` as a `code` extension. Endpoints call
   `result.Error!.ToProblem()`; one returning `Results.BadRequest` directly makes the enum
   decorative again. Covered by `Tests/Users/UsersApi`, verified by mutation.
6. **Users.Api is the only JWT issuer.** No other service creates or signs tokens; no other service
   stores password hashes. Signing keys come from `AuthOptions` via configuration, never a literal.
7. **A refresh token must outlive the access token it renews.** Equal lifetimes make refresh
   pointless — the refresh token dies at the same moment as the token it exists to replace.
8. **Refresh tokens are stored hashed**, like passwords. They are bearer credentials: a leaked
   table of plaintext refresh tokens is a leaked table of live sessions.
9. **The caller's identity comes from the token, not the request body.** Taking a user id from a
   payload lets a caller nominate whose session to act on, leaving only the token comparison as a
   guard and turning the endpoint into an oracle for which ids exist.
10. **Re-hash on login when the hasher asks for it.** `VerifyHashedPassword` returns
    `SuccessRehashNeeded` when the stored hash uses outdated parameters; treat it as success and
    persist a fresh hash, otherwise the iteration count never moves.
11. **The gateway's introspection endpoint is a public contract.** `api/users/auth` is called on
    every authenticated request in the system. Changing its shape breaks the gateway — see the
    `api-gateway` skill for the agreed request and response.

## Adding a feature slice

1. Create `Features/<Area>/<Feature>/<Feature>.cs`.
2. Static class named for the intent, wrapping `Command`/`Query`, `Response`, and an
   `internal sealed Handler`.
3. Handler returns `Result<Response>`; thread the `CancellationToken` through every async call.
4. Add a `public sealed` endpoint class implementing `IEndpointMarker` in the same file.
5. Register nothing — Scrutor discovers the endpoint, MediatR discovers the handler.
6. If the slice needs a schema change, add a migration and read the generated file before
   committing.

## Auth-security posture

Implemented:

- **Refresh tokens are stored hashed** (SHA-256), never in plaintext — a leaked table is not a leaked
  set of live sessions (rule 8).
- **The refresh endpoint derives the user by looking up the hashed token**, not from a `UserId` in the
  request body (rule 9). It returns a single uniform failure for a missing, invalid or expired token,
  so it does not leak which case occurred.
- **The refresh token outlives the access token.** The access token expires in 1 day; the refresh
  token has a longer lifetime, so it can still renew after the access token dies (rule 7).
- **Register handles the unique-constraint race** by catching `DbUpdateException` from the insert and
  returning 400, rather than letting the loser of two concurrent registrations surface as a 500
  (`efcore` rule 4).
- **The signing key is not committed.** `AuthConfigs:Token` is empty in `appsettings.json`; supply it
  via user-secrets locally or the `AuthConfigs__Token` environment variable. `Program.cs` throws at
  startup if it is missing, so the service never boots with an unusable empty key.

## Roles

`User.Role` is a `UserRole` enum (`Customer` / `Admin`), stored as its name via `HasConversion<string>()`.

- **Bootstrap: the first account ever registered becomes `Admin`**; everyone after is `Customer`. The
  Register handler decides this by checking whether the `Users` table is empty — there is no seeder and
  no admin credential in config. (A one-time race: two simultaneous first registrations could both win
  admin. Acceptable for bootstrap.)
- **`PUT api/users/{id:guid}/role`** (the `SetRole` slice) promotes or demotes, behind the `AdminOnly`
  policy (`RequireRole("Admin")`). Because Users.Api validates its own JWT — which carries the role
  claim `TokenService` issues — this is a real token check, not a trusted header.
- The role rides into the access token as a claim and is echoed by `Introspect`, which is how the
  gateway learns it and propagates `X-Identity-Role` for Bookings' admin-gated ticket endpoint.

## Known gaps

- `JwtSecurityTokenHandler` is the legacy handler; `JsonWebTokenHandler` from
  `Microsoft.IdentityModel.JsonWebTokens` is the current one and does not rewrite claim types into
  long URIs. Migrating means rebuilding token creation around `SecurityTokenDescriptor`, so it is
  deferred rather than done as a drop-in.
- The role-touching paths (first-user-is-admin in Register, the `SetRole` endpoint) have no automated
  test — Users has no DB test harness, and this repo avoids in-memory EF providers. A Users
  integration project is the right home for them.

## Common mistakes

| Symptom | Cause |
|---|---|
| Every error surfaces as 400 | An endpoint returning `Results.BadRequest` instead of `ToProblem()` (rule 5) |
| Client can't refresh after the access token dies | Refresh token has the same lifetime (rule 7) |
| Gateway returns 401 for valid credentials | `api/users/auth`'s response shape changed |
| Two features drift apart doing the same thing | Shared logic never promoted to the area or `Shared/` (rule 1) |
