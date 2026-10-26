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

    // Idempotent per payment order: asking again for an order already refunded returns that refund, never a
    // second one. Throws InvalidRequest when the provider holds no successful payment for the order.
    Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken cancellationToken = default);

    // Where a refund the provider accepted has got to. Null when the provider has no such refund.
    Task<RefundResult?> LookupRefundAsync(RefundLookupRequest request, CancellationToken cancellationToken = default);

    // The charges, refunds and refund failures the provider settled in the window. Fees, payouts and adjustments are
    // left out: they are not money this service recorded.
    Task<IReadOnlyList<SettlementLine>> GetSettlementAsync(SettlementRequest request, CancellationToken cancellationToken = default);

    // Verifies the signature before reading anything; throws PaymentProviderException with
    // InvalidSignature if it fails. Null for a verified event that is about neither a pay-in nor a refund.
    WebhookEvent? ParseWebhook(WebhookRequest request);
}
