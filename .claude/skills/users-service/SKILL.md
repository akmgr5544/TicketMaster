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
5. **`ErrorType` determines the status code.** `NotFound` → 404, `BadRequest` → 400,
   `Unauthorized` → 401, `Forbidden` → 403. An endpoint that returns `BadRequest` for every failure
   makes the enum decorative.
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

## Known gaps

Current code does not yet match the rules above.

- **No endpoint is ever mapped.** `ServiceCollectionExtension` registers every `IEndpointMarker`
  via Scrutor, but nothing resolves them and calls `MapEndpoint`, and `Program.cs` never calls a
  `MapEndpoints()`. The service currently serves zero routes.
- **`api/users/auth` does not exist**, so the gateway's introspection call cannot succeed.
- **`PasswordHash` is capped at 60 chars** (`UserConfiguration.cs:21`) but `PasswordHasher<User>`
  emits exactly 84. The first registration will fail with
  `value too long for type character varying(60)`.
- `Program.cs` calls `AddDbContext` inline while `ServiceCollectionExtension.AddDatabase` sits
  unused and the call to it is commented out. Two sources of truth, one dead.
- `Program.cs` calls `UseAuthentication()` but never `UseAuthorization()`.
- Access token and refresh token both expire in 1 day (rule 7).
- Refresh tokens are stored in plaintext (rule 8).
- `UserRefreshToken.Command` takes `UserId` from the request body (rule 9).
- `AuthenticateUser` ignores `SuccessRehashNeeded` (rule 10).
- Every failure returns `BadRequest` regardless of `ErrorType` (rule 5), and `Error` is constructed
  with the sentence in `Code` and `""` in `Message` (rule 4).
- `Result` and `Result<T>` expose public setters, so a caller can flip `IsSuccess` after the fact.
- `TokenService.CreateRefreshToken` accepts `User` and `AuthOptions` and uses neither.
- `UsersDomainContext` takes the non-generic `DbContextOptions` (see `efcore` rule 14).
- `JwtSecurityTokenHandler` is the legacy handler; `JsonWebTokenHandler` from
  `Microsoft.IdentityModel.JsonWebTokens` is the current one and does not rewrite claim types into
  long URIs.

## Common mistakes

| Symptom | Cause |
|---|---|
| Endpoint returns 404 though the feature exists | `MapEndpoint` never called — nothing maps discovered endpoints |
| Registration fails on insert | `PasswordHash` column shorter than the 84-char hash |
| Every error surfaces as 400 | `ErrorType` not mapped to status codes (rule 5) |
| Client can't refresh after the access token dies | Refresh token has the same lifetime (rule 7) |
| Gateway returns 401 for valid credentials | `api/users/auth` missing or its response shape changed |
| Two features drift apart doing the same thing | Shared logic never promoted to the area or `Shared/` (rule 1) |
