# gRPC Reference

Library-level detail underneath the `rpc` skill. Read this when writing a `.proto`, wiring a client
or server, or reasoning about Protobuf compatibility — not needed to decide *whether* a call should
be an RPC, which the skill covers.

Condensed from the official ASP.NET Core gRPC docs (view `aspnetcore-10.0`), retrieved 2026-09-05.

- Overview: https://learn.microsoft.com/aspnet/core/grpc/
- Deadlines and cancellation: https://learn.microsoft.com/aspnet/core/grpc/deadlines-cancellation
- Error handling: https://learn.microsoft.com/aspnet/core/grpc/error-handling
- Client factory: https://learn.microsoft.com/aspnet/core/grpc/clientfactory
- Interceptors: https://learn.microsoft.com/aspnet/core/grpc/interceptors
- Versioning: https://learn.microsoft.com/aspnet/core/grpc/versioning
- Troubleshooting: https://learn.microsoft.com/aspnet/core/grpc/troubleshoot

## The model

A `.proto` file is the contract. `Grpc.Tools` runs at build time and generates, per project and per
the `GrpcServices` attribute, a service base class to inherit (`Server`) and a concrete client stub
to call (`Client`). Both sides compile against the same file, which is why it is shared rather than
copied.

Transport is HTTP/2, payloads are Protobuf binary. Four call shapes exist — unary,
server-streaming, client-streaming, bidirectional — and this system uses only unary.

## Packages

| Package | Goes in | For |
|---|---|---|
| `Grpc.AspNetCore` | the service that implements the contract | server hosting; brings `Grpc.Tools` transitively |
| `Grpc.Net.ClientFactory` | the calling service | `AddGrpcClient<T>` DI registration |
| `Grpc.Tools` | any project generating from `.proto` directly | codegen only; `PrivateAssets="all"` |
| `Grpc.AspNetCore.Server.ClientFactory` | a service that calls *while handling* a gRPC call | supplies `EnableCallContextPropagation()` |

Versions belong in the root `Directory.Packages.props`. Generated code goes to `obj/`.

## Server

```csharp
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<DomainExceptionInterceptor>();
});

app.MapGrpcService<EventsGrpcService>();
```

The service class inherits the generated base and overrides its methods:

```csharp
public override async Task<EventReply> GetEvent(EventRequest request, ServerCallContext context)
{
    // context.CancellationToken is raised when the caller's deadline is exceeded — pass it on,
    // or the call keeps running server-side after the caller has already given up.
    var @event = await _repository.GetAsync(request.EventId, context.CancellationToken);
    ...
}
```

`AddGrpc` options worth knowing: `EnableDetailedErrors` (exception detail in the response — do not
enable in production), `MaxReceiveMessageSize` / `MaxSendMessageSize`, `IgnoreUnknownServices`.

## Client

```csharp
builder.Services
    .AddGrpcClient<Events.EventsClient>(o => o.Address = new Uri(configuredAddress))
    .AddInterceptor<TransportFaultInterceptor>();
```

`AddGrpcClient` is a typed `HttpClient` registration underneath, so the `HttpMessageHandler`
pipeline, DI and configuration all behave as they do for any typed client. It registers the
*generated* client; per rule 6 of the skill, wrap it behind the service's own interface rather than
injecting it into a handler.

## Deadlines and cancellation

The single most important section here.

- A deadline is set per call via `CallOptions.Deadline`. **There is no default — a call with no
  deadline is not time-limited.**
- A deadline is an absolute UTC time, not a duration: `DateTime.UtcNow.AddSeconds(5)`. A past time
  makes the call fail immediately.
- Both ends track it independently. A call can succeed on the server and still exceed the deadline
  before the response lands.
- On exceed: the client aborts the HTTP request and throws `DeadlineExceeded`; the server has its
  `ServerCallContext.CancellationToken` raised, **but the method keeps running until it returns**.
  Passing that token into every async call is what actually frees the server's resources.
- With retries configured, the deadline spans all attempts — it is not restarted per attempt.

Propagation between services is manual by default and easy to forget:

```csharp
// Inside a gRPC service, calling onward: pass the deadline you were given.
var reply = await _client.GetUserAsync(request, deadline: context.Deadline);
```

`EnableCallContextPropagation()` (from `Grpc.AspNetCore.Server.ClientFactory`) does this
automatically for clients created by the factory, forwarding both deadline and cancellation token,
and always keeping the *smaller* deadline when the child call specifies its own. It raises an error
if the client is used outside a gRPC call context; `SuppressContextNotFoundErrors = true` disables
that check.

**Only applies when the caller is itself handling a gRPC call.** In this system the callers are a
MediatR handler and a YARP auth handler, so deadlines are set explicitly from the caller's own
budget and its `CancellationToken` is passed via `CallOptions.CancellationToken`.

## Errors

There are no HTTP status codes at this layer. A call carries a `Status` — a `StatusCode` plus an
optional string detail — and the client throws `RpcException` when it is not `OK`.

`RpcException` arrives for four distinct reasons, and they need different handling:

| Cause | Status | Means |
|---|---|---|
| Server returned an error status | whatever the server chose | the callee rejected the request |
| Client could not reach the server | `Unavailable` | the callee is down or unreachable |
| The caller's `CancellationToken` fired | `Cancelled` | the caller gave up |
| The deadline passed | `DeadlineExceeded` | it took too long |

Only the first is about the request. The other three are transport conditions, and treating them as
"not found" or "invalid" is how a stale cache or a dropped connection turns into a wrong answer.

Built-in errors carry a status code and a string only. Structured error detail requires rich error
handling (`Google.Rpc.Status`), which is extra machinery — take it only when a string is genuinely
not enough.

Suggested mapping for this system, applied by interceptors on both ends per rule 7 of the skill:

| Domain | Status |
|---|---|
| `NotFoundException` | `NotFound` |
| validation / bad argument | `InvalidArgument` |
| domain rule violation | `FailedPrecondition` |
| unauthenticated caller | `Unauthenticated` |
| authenticated but not allowed | `PermissionDenied` |
| anything unhandled | `Internal`, detail scrubbed |

## Interceptors

Both ends implement the same `Interceptor` base class, overriding the methods for the call shapes in
use — `UnaryServerHandler` on the server, `AsyncUnaryCall` on the client. Each receives a
`continuation` delegate; awaiting it in a `try`/`catch` is what lets an interceptor turn a thrown
exception into a status, or a status back into an exception.

- Server: `options.Interceptors.Add<T>()` in `AddGrpc`, or `AddServiceOptions<TService>` for one
  service. Global interceptors run before per-service ones, in registration order.
- Client: `.AddInterceptor<T>()` on `AddGrpcClient`.
- **Lifetimes differ and this bites.** Server interceptors are per-request by default; client
  interceptors are created once and shared. A client interceptor needing scoped services must be
  registered with `InterceptorScope.Client`.
- Client interceptors run in reverse order of chaining.

Interceptors are not middleware. Middleware runs first, for all HTTP requests, and sees only bytes;
an interceptor sees the deserialized message and the `ServerCallContext`, and can catch exceptions
thrown by the service method. Error mapping belongs in an interceptor.

## Protobuf and versioning

Field numbers — not names — identify fields on the wire. Everything below follows from that.

**Safe (wire-compatible and binary-compatible):**
- Adding a service, a method, or a field to a request or response message. Unset fields deserialize
  to their default, so the receiver must behave correctly when a new field is absent.
- Adding a value to an enum, provided older clients cope with a value they cannot name.

**Wire-safe but breaks anyone recompiling against the new contract:**
- Removing a field. Old values land in the message's unknown fields. **`reserved` the number and the
  name** so it can never be reused — a reused number silently misinterprets old payloads.
- Renaming a field. Names are codegen-only for Protobuf. (Not true if JSON transcoding is in play.)
- Renaming or re-nesting a message, or changing `csharp_namespace`.

**Breaks the wire — never do these to a published contract:**
- Changing a field's number.
- Changing a field's type to an incompatible one.
- Renaming a package, service or method, or removing a method — callers get `Unimplemented`.

**Breaks behaviour without breaking the wire:** adding a field and then rejecting requests that omit
it. Old callers cannot set it. This is the failure that passes every compatibility check.

For a genuinely breaking change, version the package — `package events.v1;` → `events.v2;` — and
host both service implementations side by side. Generated types differ per package, so shared logic
has to sit behind a mapping layer.

Style: `.proto` uses `underscore_separated_names`; the generator produces .NET `PascalCase`. Note
that proto3 has no `required`, and an unset `string` deserializes to `""`, never `null` — which
matters in this repo, where `Directory.Build.props` sets `Nullable=enable` and the surrounding C#
records treat null as meaningful.

## Hosting notes

- gRPC needs HTTP/2 end to end. Kestrel must expose an HTTP/2 endpoint, and anything in the path
  must proxy HTTP/2 rather than downgrade.
- YARP proxies gRPC, but only with HTTP/2 preserved to the destination. gRPC-Web is a separate
  protocol needed only for browsers.
- A `GrpcChannel` built over an `HttpClient` inherits that client's 100-second default timeout,
  which cancels long calls independently of any deadline. Set `GrpcChannelOptions.HttpHandler`
  rather than `HttpClient`, or raise the timeout.
- Hosting a gRPC server outside a web SDK project needs
  `<FrameworkReference Include="Microsoft.AspNetCore.App" />`.
