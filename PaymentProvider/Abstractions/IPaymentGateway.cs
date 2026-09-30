using PaymentProvider.Models;

namespace PaymentProvider.Abstractions;

public interface IPaymentGateway
{
    PaymentProviderKind Kind { get; }

    Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken = default);

    // Null when the client hands the payment method to the provider directly and the outcome
    // arrives by webhook (Stripe). Braintree charges here and returns the outcome synchronously.
    Task<PaymentResult?> SubmitPaymentMethodAsync(SubmitPaymentMethodRequest request, CancellationToken cancellationToken = default);

    // Null when the provider has no payment for the order yet.
    Task<PaymentResult?> LookupAsync(PaymentLookupRequest request, CancellationToken cancellationToken = default);

    // Verifies the signature before reading anything; throws PaymentProviderException with
    // InvalidSignature if it fails. Null for a verified event that is not about a pay-in.
    WebhookEvent? ParseWebhook(WebhookRequest request);
}
