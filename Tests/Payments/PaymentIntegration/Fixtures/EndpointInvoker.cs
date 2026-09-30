using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using PaymentSystem.Shared.Endpoints;

namespace PaymentIntegration.Fixtures;

public sealed record EndpointResponse(int Status, string Body);

// Runs a slice's real endpoint delegate — built by ASP.NET's own RequestDelegateFactory from the slice's
// MapPost — against the fixture's container, with no server. That keeps the parts only the endpoint owns
// under test (the identity header, route and body binding, raw webhook body, status mapping) without a second
// host that would need its own copy of the fixture's wiring.
internal sealed class EndpointInvoker : IEndpointRouteBuilder
{
    private readonly RequestDelegate _handler;

    public EndpointInvoker(IServiceProvider services, IEndpointMarker endpoint)
    {
        ServiceProvider = services;
        endpoint.MapEndpoint(this);
        _handler = DataSources.SelectMany(source => source.Endpoints).Single().RequestDelegate!;
    }

    public IServiceProvider ServiceProvider { get; }

    public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

    public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);

    public async Task<EndpointResponse> PostAsync(IReadOnlyDictionary<string, string> routeValues,
        string? body = null, IReadOnlyDictionary<string, string>? headers = null, string contentType = "application/json")
    {
        await using var scope = ServiceProvider.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Method = HttpMethods.Post;
        foreach (var (key, value) in routeValues)
            context.Request.RouteValues[key] = value;
        foreach (var (key, value) in headers ?? new Dictionary<string, string>())
            context.Request.Headers[key] = value;
        if (body is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            context.Request.Body = new MemoryStream(bytes);
            context.Request.ContentLength = bytes.Length;
            context.Request.ContentType = contentType;
            // A server sets this per request; without it binding treats the body as absent.
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new HasBody());
        }

        var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await _handler(context);

        return new EndpointResponse(context.Response.StatusCode, Encoding.UTF8.GetString(responseBody.ToArray()));
    }

    private sealed class HasBody : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
