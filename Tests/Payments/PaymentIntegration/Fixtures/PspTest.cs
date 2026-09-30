using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentProvider.Models;
using PaymentSystem.Domain;
using PaymentSystem.Features.Webhooks;
using PaymentSystem.Shared.Endpoints;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Fixtures;

public abstract class PspTest : IntegrationTest
{
    private readonly PaymentsFixture _fixture;

    protected PspTest(PaymentsFixture fixture) : base(fixture)
    {
        _fixture = fixture;
        Psp = fixture.Services.GetRequiredService<StubPsp>();
        Logs = fixture.Services.GetRequiredService<LogCapture>();
        Psp.Reset();
        Logs.Clear();
    }

    protected StubPsp Psp { get; }

    protected LogCapture Logs { get; }

    protected string ConnectionString =>
        _fixture.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")!;

    protected static readonly IReadOnlyDictionary<string, string> Signed =
        new Dictionary<string, string> { [StubGateway.SignatureHeader] = StubGateway.ValidSignature };

    // Each send in a scope of its own, the way each HTTP request gets one.
    protected async Task<Result<T>> SendAsync<T>(IRequest<Result<T>> request)
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(request);
    }

    internal EndpointInvoker Endpoint(IEndpointMarker endpoint) => new(_fixture.Services, endpoint);

    protected static Dictionary<string, string> As(Guid callerId) =>
        new() { [CallerIdentity.UserIdHeader] = callerId.ToString() };

    protected static string WebhookBody(Guid? paymentOrderId, PaymentStatus? status, string? eventId = null) =>
        new StubWebhookBody(eventId ?? $"evt_{Guid.NewGuid():N}", paymentOrderId, status).ToJson();

    protected Task<Result<HandleWebhook.Response>> WebhookAsync(Guid paymentOrderId, PaymentStatus status,
        string? eventId = null, string provider = "stripe") =>
        SendAsync(new HandleWebhook.Command(provider, WebhookBody(paymentOrderId, status, eventId), Signed));

    protected async Task<PaymentOrder> OrderAsync(PaymentEvent checkout, int index) =>
        await ReadOrderAsync(checkout.OrderId(index));
}
