# Packages — What the Repo Uses, and What to Reach For

> Last reviewed: 2026-10-01. Versions live only in `Directory.Packages.props` (production) and
> `Tests/Directory.Packages.props` (test-only, imports the root). Never put `Version=` on a
> `<PackageReference>`.
>
> **Never trust a version number from memory, including the ones below.** Before adding or
> upgrading, check `dotnet package search <name>` or NuGet.org, and match `Microsoft.*` packages to
> the 10.0.x line already pinned.

## In use

| Concern | Package | Used by | Notes |
|---|---|---|---|
| In-process CQRS | `MediatR` 14.1.0 | Bookings, Events, Users, Payments | License caveat — ADR-005 |
| Messaging + outbox | `WolverineFx` (+ `.RabbitMQ`, `.Postgresql`, `.EntityFrameworkCore`, `.CosmosDb`) 5.41.0 | Bookings, Payments, Events | 6.x upgrade not started; `messaging` skill |
| ORM | `Microsoft.EntityFrameworkCore` 10.0.x, `Npgsql.EntityFrameworkCore.PostgreSQL` | Users, Bookings, Payments | ADR-003, `efcore` skill |
| Document store | `Microsoft.Azure.Cosmos` | Events | `document-db` skill |
| Redis | `Microsoft.Extensions.Caching.StackExchangeRedis` (brings `StackExchange.Redis`), `DistributedLock.Redis` | Bookings | Reservations + locks, ADR-004 |
| RPC | `Grpc.AspNetCore`, `Grpc.Net.ClientFactory`, `Grpc.Tools`, `Google.Protobuf` | Bookings → Events | `rpc` skill |
| Gateway | `Yarp.ReverseProxy` | ApiGateway | `api-gateway` skill |
| Auth | `Microsoft.AspNetCore.Authentication.JwtBearer`, `System.IdentityModel.Tokens.Jwt` | Users | Users issues and introspects; nothing else validates JWTs |
| PSPs | `Stripe.net`, `Braintree` | PaymentProvider | Behind the anti-corruption layer only |
| DI scanning | `Scrutor` | Users, Payments | Endpoint discovery via `IEndpointMarker` |
| OpenAPI | `Microsoft.AspNetCore.OpenApi` | APIs | Built-in; no Swashbuckle |
| JSON (legacy) | `Newtonsoft.Json` | `Events.Cosmos` (the Cosmos SDK needs it but its nuspec omits it — keep the explicit reference); `Bookings.Application` `CacheService` (reservation serialization) | Use `System.Text.Json` for anything new. Switching `CacheService` changes the stored format of live reservations — do it only with a TTL-length cutover |
| Analysis | `SonarAnalyzer.CSharp` | every project (`GlobalPackageReference`) | Don't suppress globally; fix or suppress at the line with a reason |
| Tests | `xunit` 2.9.3, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `coverlet.collector` | all test projects | xUnit built-in `Assert` only |
| Arch tests | `TngTech.ArchUnitNET.xUnit` | `*Architecture` projects | |
| Integration | `Testcontainers` (+ `.PostgreSql`, `.Redis`, `.RabbitMq`), `Respawn`, `Microsoft.AspNetCore.Mvc.Testing` | `*Integration`, `GatewayTests`, `GrpcSeam` | `testing` skill |

## Reach for these when the need appears

| Need | Package | Why this one | Don't use when |
|---|---|---|---|
| Controllable time in tests outside Payments | `Microsoft.Extensions.TimeProvider.Testing` (`FakeTimeProvider`) | Official; replaces copying `ControllableTimeProvider` | Payments — it already has its own; don't run two |
| Resilience on the gateway's `"UsersService"` client | `Microsoft.Extensions.Http.Resilience` (`AddStandardResilienceHandler`) | Polly v8 on `IHttpClientFactory` | Wolverine handlers — Wolverine has its own retry policies |
| Tracing across gateway → services → RabbitMQ | `OpenTelemetry.Extensions.Hosting` + ASP.NET Core / HttpClient / gRPC instrumentation + OTLP exporter | Wolverine and Npgsql emit `ActivitySource` spans OTel picks up | — |
| Structured log sinks | `Serilog.AspNetCore` | Rich sinks and enrichers | Plain console logging is enough for compose |
| Fluent assertions | `Shouldly` or `AwesomeAssertions` | MIT/Apache | Not needed — the suite uses `Assert`. **Never FluentAssertions v8+** (commercial) |
| Snapshot tests for ProblemDetails/JSON payloads | `Verify.Xunit` | Diffable `.verified.` files | Small payloads where `Assert.Equal` is clearer |
| HTTP fakes for a third-party API | `WireMock.Net` | Latency/failure simulation | PaymentAdapters already has `FakeHttpServer`; extend that first |
| Mapping | none — manual mapping | Transparent, refactor-safe | If mapping volume explodes, `Riok.Mapperly`. **Never AutoMapper 15+** (commercial) |
| Validation library | none today | Handlers/domain validate; ADR-002 paths report it | Add `FluentValidation` only with a pipeline behavior and a reason |

## Avoid

- **MassTransit 9+** — commercial; Wolverine already covers messaging.
- **AutoMapper 15+, FluentAssertions 8+** — commercial.
- **`System.Linq.Async`** — now in the BCL; causes ambiguous calls.
- **A second mediator or bus** alongside MediatR + Wolverine.
- **Swashbuckle** — built-in OpenAPI is already in use.
