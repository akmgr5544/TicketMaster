---
name: api-gateway
description: Use when working on TicketMaster.ApiGateway — YARP routes or clusters, edge authentication, the Users.Api introspection call, identity header propagation, or putting a new service behind the gateway.
---

# TicketMaster API Gateway

The gateway is the single edge of the system. It authenticates every request by delegating to
Users.Api, then proxies to one of the downstream services with the caller's identity attached
as headers. It holds no business logic and no database.

## Scope

Covers `TicketMaster.ApiGateway/` only.

Each of the four services (Users, Bookings, Events, and the shared kernel) has its own
architecture and its own skill. When work moves from routing/edge-auth into a service's
domain, application, or infrastructure layer, **stop and load that service's skill** — the
layering rules there do not apply here, and the rules here do not apply there.

## YARP reference

**Read `yarp-reference.md` in this skill directory before editing `yarp.routes.json`,
`yarp.clusters.json`, or anything in `Transforms/`.** It holds the verified route and cluster
schema, the full transform list with exact config keys, the authorization policy special values,
and the `ITransformProvider` code API — condensed from the official Microsoft Learn YARP docs. Do
not guess config keys or transform names from memory; they are easy to get subtly wrong and a bad
route reloads silently rather than failing at startup.

## Request flow

```
client
  │  Authorization: Bearer <jwt>
  ▼
Gateway route match (yarp.routes.json)
  │
  ├── route has no AuthorizationPolicy ──────────────► proxy anonymously
  │
  └── route has AuthorizationPolicy
        │
        ▼
      UsersServiceAuthHandler  ── GET api/users/auth ──►  Users.Api
        │                       ◄── identity + permissions ──
        │  builds ClaimsPrincipal
        ▼
      Authorization policy evaluated (GatewayAuthPolicy, permission policies)
        │
        ▼
      AuthTransformProvider  — sets X-Identity-* headers, strips client-supplied ones
        │
        ▼
      Cluster destination (yarp.clusters.json), path prefix stripped
```

The JWT never travels past the gateway as a trust signal. Downstream services trust the
`X-Identity-*` headers.

## File map

| File | Owns |
|---|---|
| `Program.cs` | Composition only: HttpClient, auth scheme, policies, YARP config load, transform registration |
| `Handlers/UsersServiceAuthHandler.cs` | The introspection call and claim construction |
| `Transforms/AuthTransformProvider.cs` | Identity propagation onto the proxied request |
| `YarpConfigurations/yarp.routes.json` | Path matching, policy assignment, path-strip transform |
| `YarpConfigurations/yarp.clusters.json` | Destination addresses only |
| `Dtos/UserDto.cs` | The introspection response contract |

The two YARP files are split deliberately to keep each one short. The cost of that split is
that route→cluster and route→policy references cross file boundaries as bare strings; see
*Adding a downstream service*.

## Auth contract (target)

Gateway → Users.Api:

```
GET api/users/auth
Authorization: Bearer <token>
```

Response `200`:

```json
{
  "id": "...",
  "email": "...",
  "firstName": "...",
  "lastName": "...",
  "userName": "...",
  "permissions": ["bookings.write", "events.read"]
}
```

Any non-2xx means unauthenticated — return `AuthenticateResult.Fail`, never a partial principal.

Claims built from the response: `UserId`, `Email`, `FirstName`, `LastName`, `UserName`, and one
`Permission` claim per entry in `permissions`.

Policies in `Program.cs`:
- `GatewayAuthPolicy` — `RequireAuthenticatedUser()`, the default for protected routes.
- Permission policies — `RequireClaim("Permission", "<value>")`, one per coarse-grained capability.

Coarse-grained checks belong here. Fine-grained checks ("is this *your* booking?") stay in the
owning service.

## Rules

1. **Never validate JWT signatures at the gateway.** Introspection via Users.Api is the only
   auth path. No `AddJwtBearer`, no `TokenValidationParameters`, no signing key in gateway config.
2. **Public routes set `"AuthorizationPolicy": "Anonymous"` explicitly.** Login, register, and
   refresh must be reachable without a token — a client cannot obtain one otherwise. Do not just
   omit the property: an omitted policy falls through to `FallbackPolicy`, while `"Anonymous"`
   disables authorization for that route regardless of any fallback. Give the public route an
   explicit `Order` lower than the catch-all route so matching is unambiguous.
3. **Identity headers are set, never appended.** YARP's documented default: *"All incoming request
   headers are copied to the proxy request by default with the exception of the Host header."*
   `HttpHeaders.Add` appends rather than replaces, so a client-supplied `X-Identity-UserId`
   survives and lands **before** the trusted value. Remove first, then set. YARP's own
   `X-Forwarded` transform does exactly this — it removes an existing header even when it has no
   value to write, specifically to prevent spoofing. Copy that pattern.
4. **Downstream services read identity from `X-Identity-*` only.** They do not read
   `Authorization` and do not re-validate the token.
5. **Routing is data, not code.** A new service is a cluster entry plus a route entry. Never
   hardcode a route or a destination in `Program.cs`.
6. **Keep the file split clean.** `yarp.clusters.json` holds destinations only.
   `yarp.routes.json` holds match, policy, and transforms only.
7. **No business logic in the gateway.** No DbContext, no MediatR, no domain types. The only
   outbound call is the introspection call.
8. **Central package management.** Add versions to `Directory.Packages.props`, never
   `Version="..."` in `TicketMaster.ApiGateway.csproj`.

## Adding a downstream service

1. Add the cluster to `yarp.clusters.json`:
   ```json
   "payments-cluster": {
     "Destinations": { "destination1": { "Address": "http://payments-api:8080/" } }
   }
   ```
2. Add the route to `yarp.routes.json`:
   ```json
   "payments-route": {
     "ClusterId": "payments-cluster",
     "AuthorizationPolicy": "GatewayAuthPolicy",
     "Match": { "Path": "/payments-service/{**catch-all}" },
     "Transforms": [ { "PathPattern": "{**catch-all}" } ]
   }
   ```
   `{ "PathRemovePrefix": "/payments-service" }` is an equivalent way to strip the prefix and does
   not depend on the route template capturing a remainder. Pick one and stay consistent — the
   existing three routes use `PathPattern`.
3. The new service reads `X-Identity-UserId` / `X-Identity-UserName`, and does not re-validate
   the token.
4. Nothing changes in `Program.cs` unless the route needs a **new** named policy.

### Invariants (test-enforced)

These cross-file references are validated by tests over the loaded configuration, not by hand:

- every route's `ClusterId` resolves to a key in `yarp.clusters.json`
- every route's `AuthorizationPolicy` resolves to a policy registered in `Program.cs`
- every route with a path prefix has a `PathPattern` transform that strips it

Test projects are grouped by service under `Tests/<Service>/`, so gateway tests belong in
`Tests/Gateway/`.

## Common mistakes

| Symptom | Cause |
|---|---|
| App fails at startup naming a route | `ClusterId` doesn't match a cluster key |
| Unhandled error on one route, not a clean 401 | `AuthorizationPolicy` name has no matching `AddPolicy` |
| Gateway looks fine, service returns 404 | Missing `PathPattern` transform — prefix forwarded intact |
| 503 at request time | Cluster `Address` is unreachable |
| 401 on login or registration | The anonymous routes below were removed or lost precedence |
| Downstream sees the wrong user | Identity header appended instead of replaced (rule 3) |
| Client cannot log in | Public route missing or carrying an `AuthorizationPolicy` (rule 2) |

## Known gaps

Current code does not yet match this target. Do not read the rules above as descriptions of
what exists.

- No permission model on the gated routes — see below. The users route carries no
  `AuthorizationPolicy`, so login, registration and refresh are reachable through the gateway; that is
  deliberate and the table further down explains why.
- `Program.cs` never calls `app.UseAuthentication()` / `app.UseAuthorization()` before
  `MapReverseProxy()`. `WebApplication` auto-inserts both when the services are registered, so this
  works today, but the YARP docs specify them explicitly and relying on the implicit insertion
  makes middleware ordering invisible.
- No permission model yet — `GatewayAuthPolicy` is `RequireAuthenticatedUser()` only, so any
  authenticated caller reaches every proxied endpoint, including `POST /bookings-service/api/tickets`,
  which is meant for admins.
- Nothing verifies the routing. There is no gateway test project, so the anonymous-route precedence
  below is reasoned from YARP's matching rules, not observed.
- No caching of introspection results: every proxied request costs an extra call to Users.Api.

## Who enforces authentication, per cluster

One route per service, no endpoint ever named in gateway config. Which cluster carries
`GatewayAuthPolicy` follows from whether the service behind it can defend itself.

| Cluster | Gateway policy | Why |
|---|---|---|
| `users-cluster` | **none** | Users.Api validates JWTs itself and each endpoint declares its own requirement — only `api/users/auth` has `.RequireAuthorization()` |
| `bookings-cluster` | `GatewayAuthPolicy` | Bookings never validates a token; it trusts `X-Identity-UserId` |
| `events-cluster` | `GatewayAuthPolicy` | same |

**Do not put `GatewayAuthPolicy` on the users route.** It would cover `api/users/login`,
`api/users/registration` and `api/users/refreshToken` — every way of obtaining a token — so a caller
would need a token to get one and nothing behind the gateway would ever be reachable.

The fix is *not* to enumerate the public paths as their own anonymous routes. That makes gateway
config track every endpoint the service has, and it duplicates a decision Users.Api already makes per
endpoint. Leave the cluster ungated and let the service decide; a new protected endpoint there gets
its protection from `.RequireAuthorization()`, where the rest of its rules already live.

That reasoning does **not** transfer to Bookings or Events. Neither validates a token, so removing
their policy would leave them open — the asymmetry is about which services can defend themselves, not
a general preference.

## Addresses

Destinations point at the services' **https** ports, because all three call `UseHttpsRedirection()`
and would answer a plain-http proxy request with a 307. Run every service on its `https` launch
profile; Events additionally requires it, since its gRPC endpoint needs HTTP/2 over ALPN.

| Cluster | Address |
|---|---|
| `users-cluster` | `https://localhost:7054` |
| `bookings-cluster` | `https://localhost:7225` |
| `events-cluster` | `https://localhost:7158` |

The `UsersService` client's base address is `Services:Users:BaseAddress` in the gateway's
`appsettings.json`, not a literal in `Program.cs`, and startup fails loudly if it is missing.
