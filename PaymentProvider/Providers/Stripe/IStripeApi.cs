using PaymentProvider.Contracts.Stripe;

namespace PaymentProvider.Providers.Stripe;

internal interface IStripeApi
{
    Task<StripePaymentIntent> CreatePaymentIntentAsync(StripeCreatePaymentIntent request, CancellationToken cancellationToken);

    Task<StripePaymentIntent?> GetPaymentIntentAsync(string id, CancellationToken cancellationToken);

    Task<StripePaymentIntent?> FindPaymentIntentAsync(string paymentOrderId, CancellationToken cancellationToken);

    // Returns the intent's existing refund instead of failing when it has already been refunded in full.
    Task<StripeRefund> CreateRefundAsync(StripeCreateRefund request, CancellationToken cancellationToken);

    StripeEvent ParseEvent(string body, string signatureHeader);
}
