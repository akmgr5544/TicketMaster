using PaymentProvider.Models;

namespace PaymentProvider.Abstractions;

public interface IPaymentGatewayFactory
{
    IPaymentGateway Default { get; }

    IPaymentGateway Get(PaymentProviderKind kind);

    // By the provider's name as a payment order records it. Null — an order not started yet, or started before the
    // provider was recorded — is the default, which is where every such order went.
    IPaymentGateway ForProvider(string? provider);
}
