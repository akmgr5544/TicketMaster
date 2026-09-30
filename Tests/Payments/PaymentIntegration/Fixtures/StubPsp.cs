using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentIntegration.Fixtures;

// The PSP is another process, so it is the one hop replaced — the analogue of StubEventsService in Bookings.
// Everything up to IPaymentGatewayFactory stays the production wiring. The real adapters have their own suite
// (PaymentAdapters) against a fake HTTP server; this one only needs outcomes to hand the handlers.
public sealed class StubPsp : IPaymentGatewayFactory
{
    public StubGateway Stripe { get; } = new(PaymentProviderKind.Stripe);

    public StubGateway Braintree { get; } = new(PaymentProviderKind.Braintree);

    public PaymentProviderKind DefaultKind { get; set; }

    public IPaymentGateway Default => Get(DefaultKind);

    public IPaymentGateway Get(PaymentProviderKind kind) => kind switch
    {
        PaymentProviderKind.Stripe => Stripe,
        PaymentProviderKind.Braintree => Braintree,
        _ => throw new PaymentProviderException(kind, PaymentProviderErrorKind.Configuration, "Not configured.")
    };

    public void Reset()
    {
        DefaultKind = PaymentProviderKind.Stripe;
        Stripe.Reset();
        Braintree.Reset();
    }
}

public sealed class StubGateway(PaymentProviderKind kind) : IPaymentGateway
{
    public const string SignatureHeader = "Stub-Signature";
    public const string ValidSignature = "valid";

    public PaymentProviderKind Kind => kind;

    public ConcurrentQueue<CheckoutRequest> Checkouts { get; } = new();

    public ConcurrentQueue<SubmitPaymentMethodRequest> Submissions { get; } = new();

    public Func<CheckoutRequest, CheckoutSession> OnCheckout { get; set; } = null!;

    public Func<SubmitPaymentMethodRequest, Task<PaymentResult?>> OnSubmit { get; set; } = null!;

    // Distinct per call, so a test can tell a replayed client token from the first.
    public static string ClientTokenFor(Guid paymentOrderId, int call) => $"client_secret_{paymentOrderId:N}_{call}";

    // Stable per order, the way an idempotency key replays the same session.
    public static string ReferenceFor(Guid paymentOrderId) => $"pi_{paymentOrderId:N}";

    public void Reset()
    {
        Checkouts.Clear();
        Submissions.Clear();
        OnCheckout = request => new CheckoutSession(
            kind == PaymentProviderKind.Braintree ? null : ReferenceFor(request.PaymentOrderId),
            ClientTokenFor(request.PaymentOrderId, Checkouts.Count));
        OnSubmit = request => Task.FromResult<PaymentResult?>(kind == PaymentProviderKind.Braintree
            ? new PaymentResult($"bt_{request.PaymentOrderId:N}", PaymentStatus.Succeeded)
            : null);
    }

    public Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default)
    {
        Checkouts.Enqueue(request);
        return Task.FromResult(OnCheckout(request));
    }

    public Task<PaymentResult?> SubmitPaymentMethodAsync(SubmitPaymentMethodRequest request,
        CancellationToken cancellationToken = default)
    {
        Submissions.Enqueue(request);
        return OnSubmit(request);
    }

    public Task<PaymentResult?> LookupAsync(PaymentLookupRequest request, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    // Signed by a header, as the real ones are: anything but the valid signature is refused before the body is read.
    public WebhookEvent? ParseWebhook(WebhookRequest request)
    {
        if (request.GetHeader(SignatureHeader) != ValidSignature)
            throw new PaymentProviderException(kind, PaymentProviderErrorKind.InvalidSignature, "Bad signature.");

        var body = JsonSerializer.Deserialize<StubWebhookBody>(request.Body, JsonSerializerOptions.Web)!;
        return body.Status is not { } status
            ? null
            : new WebhookEvent(body.EventId, body.PaymentOrderId,
                new PaymentResult(body.PaymentOrderId is { } id ? ReferenceFor(id) : "pi_unknown", status));
    }
}

// Status null stands for a verified event that is not about a pay-in.
public sealed record StubWebhookBody(string EventId, Guid? PaymentOrderId, PaymentStatus? Status)
{
    public string ToJson() => JsonSerializer.Serialize(this, JsonSerializerOptions.Web);
}

// Every formatted log line and exception, from every category, so a test can prove a secret never reached one.
public sealed class LogCapture : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _lines = new();

    public IReadOnlyCollection<string> Lines => _lines;

    public void Clear() => _lines.Clear();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class Logger(LogCapture owner, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            owner._lines.Enqueue($"{logLevel} {category}: {formatter(state, exception)} {exception}");
    }
}

public static class PspStubRegistration
{
    // Removed then added, not shadowed, so ValidateOnBuild has no leftover production factory to construct.
    public static IServiceCollection AddPspStubs(this IServiceCollection services)
    {
        services.RemoveAll<IPaymentGatewayFactory>();
        services.AddSingleton<StubPsp>();
        services.AddSingleton<IPaymentGatewayFactory>(sp => sp.GetRequiredService<StubPsp>());

        services.AddSingleton<LogCapture>();
        services.AddSingleton<ILoggerProvider>(sp => sp.GetRequiredService<LogCapture>());
        return services;
    }
}
