# YARP Reference

Condensed from the official Microsoft Learn YARP docs (ASP.NET Core 10 / `Yarp.ReverseProxy` 2.2),
retrieved 2026-08-08. Source pages linked per section — fetch them when this file is not enough.

- Config files: https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/config-files?view=aspnetcore-10.0
- Request transforms: https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/transforms-request?view=aspnetcore-10.0
- Auth: https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/authn-authz?view=aspnetcore-10.0
- Load balancing: https://learn.microsoft.com/aspnet/core/fundamentals/servers/yarp/load-balancing?view=aspnetcore-10.0
- `RouteConfig`: https://learn.microsoft.com/dotnet/api/yarp.reverseproxy.configuration.routeconfig
- `ClusterConfig`: https://learn.microsoft.com/dotnet/api/yarp.reverseproxy.configuration.clusterconfig

## Route matching

Most specific route wins. Explicit control via `Order` — **lower values take higher priority**.

Default match precedence when `Order` is not set: path → method → host → headers → query parameters.
A route specifying methods and no query parameters matches before one specifying query parameters
and no methods.

Required fields per route: `RouteId` (the JSON key), `ClusterId`, and `Match` containing either
`Path` or `Hosts`. `Path` uses ASP.NET Core route template syntax.

## Route properties

```json
"allrouteprops": {
  "ClusterId": "allclusterprops",
  "Order": 100,                          // Lower numbers have higher precedence
  "MaxRequestBodySize": 1000000,         // Bytes; overrides server 30MB default; -1 disables
  "AuthorizationPolicy": "Anonymous",    // Policy name, or "Default", or "Anonymous"
  "CorsPolicy": "Default",               // Policy name, or "Default", or "Disable"
  "RateLimiterPolicy": "customPolicy",   // Policy name, or "disable"
  "TimeoutPolicy": "customPolicy",       // Or "disable". Timeout+TimeoutPolicy together is invalid
  "Timeout": "00:00:30",                 // HH:MM:SS
  "Match": {
    "Path": "/something/{**remainder}",
    "Hosts": [ "www.aaaaa.com" ],        // Unspecified = any
    "Methods": [ "GET", "PUT" ],         // Unspecified = all
    "Headers": [
      {
        "Name": "MyCustomHeader",
        "Values": [ "value1", "value2" ],
        "Mode": "ExactHeader",           // Or HeaderPrefix, Exists, Contains, NotContains, NotExists
        "IsCaseSensitive": true
      }
    ],
    "QueryParameters": [
      {
        "Name": "MyQueryParameter",
        "Values": [ "value1" ],
        "Mode": "Exact",                 // Or Prefix, Exists, Contains, NotContains
        "IsCaseSensitive": true
      }
    ]
  },
  "Metadata": { "MyName": "MyValue" },   // Key/value pairs for custom extensions
  "Transforms": [ { "RequestHeader": "MyHeader", "Set": "MyValue" } ]
}
```

## Cluster properties

```json
"allclusterprops": {
  "Destinations": {
    "first_destination": { "Address": "https://contoso.com" },
    "another_destination": {
      "Address": "https://10.20.30.40",
      "Health": "https://10.20.30.40:12345/test"   // Override for active health checks
    }
  },
  "LoadBalancingPolicy": "PowerOfTwoChoices",      // Default. Or FirstAlphabetical, Random,
                                                   // RoundRobin, LeastRequests
  "SessionAffinity": {
    "Enabled": true,                               // Default false
    "Policy": "Cookie",                            // Default. Or CustomHeader
    "FailurePolicy": "Redistribute",               // Default. Or Return503Error
    "Settings": { "CustomHeaderName": "MySessionHeaderName" }
  },
  "HealthCheck": {
    "Active": {
      "Enabled": "true",
      "Interval": "00:00:10",
      "Timeout": "00:00:10",
      "Policy": "ConsecutiveFailures",
      "Path": "/api/health",
      "Query": "?foo=bar"
    },
    "Passive": {
      "Enabled": true,                             // Default false
      "Policy": "TransportFailureRateHealthPolicy",// Required
      "ReactivationPeriod": "00:00:10"
    }
  },
  "HttpClient": {
    "SSLProtocols": "Tls13",
    "DangerousAcceptAnyServerCertificate": false,
    "MaxConnectionsPerServer": 1024,
    "EnableMultipleHttp2Connections": true,
    "RequestHeaderEncoding": "Latin1",
    "ResponseHeaderEncoding": "Latin1"
  },
  "HttpRequest": {
    "ActivityTimeout": "00:02:00",
    "Version": "2",
    "VersionPolicy": "RequestVersionOrLower",
    "AllowResponseBuffering": "false"
  },
  "Metadata": { "MyKey": "MyValue" }
}
```

## Authentication and authorization

No auth is performed unless enabled per route or via `FallbackPolicy`. **Policy names are case
insensitive.**

Special `AuthorizationPolicy` values:

| Value | Meaning |
|---|---|
| `"Default"` | Uses `AuthorizationOptions.DefaultPolicy` — pre-configured to require an authenticated user |
| `"Anonymous"` | No authorization checks on this route, **regardless of `FallbackPolicy`** |
| *(unset)* | Only `FallbackPolicy` applies. `FallbackPolicy` has no value by default, so any request is allowed |

Middleware must be registered before the proxy:

```csharp
app.UseAuthentication();
app.UseAuthorization();

app.MapReverseProxy();
```

**Flowing credentials:** cookie, bearer, and API-key values sit in request headers and flow to the
destination by default — the destination still has to verify and interpret them, which is duplicated
work. Auth types that don't flow naturally must be converted in the proxy via a custom request
transform.

## Request transforms

Transforms are applied **in the order specified in the route configuration**.

**Default behavior (critical):** *"All incoming request headers are copied to the proxy request by
default with the exception of the Host header."* X-Forwarded headers are also added by default.

### Path

| Key | Effect |
|---|---|
| `{ "PathPrefix": "/prefix" }` | `/request/path` → `/prefix/request/path` |
| `{ "PathRemovePrefix": "/prefix" }` | `/prefix/request/path` → `/request/path`. Matches on `/` segment boundaries; no change if prefix doesn't match |
| `{ "PathSet": "/newpath" }` | `/request/path` → `/newpath` |
| `{ "PathPattern": "/my/{plugin}/api/{**remainder}" }` | Rebuilds the path from route values; `{}` segments without a matching route value are removed |

`PathRemovePrefix` and `PathPattern` are both valid ways to strip a route prefix. `PathPattern`
requires the route template to capture the remainder; `PathRemovePrefix` does not.

### Headers

| Key | Effect |
|---|---|
| `{ "RequestHeader": "H", "Set": "v" }` | **Replaces** any existing header `H` |
| `{ "RequestHeader": "H", "Append": "v" }` | **Adds an additional** header with that value |
| `{ "RequestHeaderRemove": "H" }` | Removes the named header |
| `{ "RequestHeadersCopy": "false" }` | Stops copying all incoming headers (default `true`) |
| `{ "RequestHeadersAllowed": "H1;H2" }` | Disables `RequestHeadersCopy` and copies only these headers |
| `{ "RequestHeaderOriginalHost": "true" }` | Copies the incoming Host header (default `false`) |
| `{ "RequestHeaderRouteValue": "H", "Set": "routeKey" }` | Sets header from a route value |

Setting `""` as a header value is not recommended and causes undefined behavior.

`RequestHeadersAllowed` note: YARP already refuses to copy connection-specific or security-sensitive
headers (`Connection`, `Alt-Svc`). Naming those in the allow list bypasses that protection and is
strongly discouraged.

### Other

| Key | Effect |
|---|---|
| `{ "QueryValueParameter": "foo", "Append": "bar" }` | Adds/replaces a query parameter with a static value |
| `{ "QueryRouteParameter": "foo", "Append": "remainder" }` | Adds/replaces a query parameter from a route value |
| `{ "QueryRemoveParameter": "foo" }` | Removes a query parameter |
| `{ "HttpMethodChange": "PUT", "Set": "POST" }` | Rewrites the HTTP method |
| `{ "X-Forwarded": "Set", "For": "Remove", ... }` | Controls `X-Forwarded-*`. Actions: Set, Append, Remove, Off. Enabled by default |
| `{ "Forwarded": "by,for,host,proto" }` | RFC 7239 header. Enabling it disables the default X-Forwarded transforms |
| `{ "ClientCert": "X-Client-Cert" }` | Base64 client cert into a header; only applies if a cert is on the connection |

Note on `X-Forwarded` with action `Set`: if the value isn't available (e.g. `RemoteIpAddress` is
null), **any existing header is still removed to prevent spoofing.** This is the pattern to copy for
any trusted header the gateway injects.

## Transforms from code

```csharp
internal sealed class ExampleTransformProvider : ITransformProvider
{
    public void ValidateRoute(TransformRouteValidationContext context) { }
    public void ValidateCluster(TransformClusterValidationContext context) { }

    public void Apply(TransformBuilderContext context)
    {
        // Equivalent to the JSON keys above:
        context.AddRequestHeaderRemove("X-Some-Header");
        context.AddRequestHeader("X-Some-Header", "value", append: false);
        context.AddPathRemovePrefix("/prefix");
        context.CopyRequestHeaders = false;

        // Arbitrary per-request logic:
        context.AddRequestTransform(transformContext =>
        {
            transformContext.ProxyRequest.Headers.Remove("X-Some-Header");
            transformContext.ProxyRequest.Headers.Add("X-Some-Header", "value");
            return ValueTask.CompletedTask;
        });
    }
}
```

`WithTransformRequestHeader(route, headerName, value, append = true)` — **`append` defaults to
`true`**. Pass `append: false` to replace.

`HttpHeaders.Add` appends; it does not replace. Always `Remove` first when the value must be trusted.

## Config loading

`LoadFromConfig` can be called multiple times against different sections, and combined with
`LoadFromMemory`. Routes can reference clusters from another source. Merging partial config for the
*same* route or cluster across sources is not supported.

```csharp
services.AddReverseProxy()
    .LoadFromConfig(Configuration.GetSection("ReverseProxy1"))
    .LoadFromConfig(Configuration.GetSection("ReverseProxy2"));
```

Config reloads without restarting the proxy when the source file changes. On reload the new config
is diffed against the current one, applied atomically, and affects only new requests. Errors during
reload are logged and suppressed — **the app keeps running on the last known good configuration**, so
a broken route change can fail silently at runtime rather than at startup.
