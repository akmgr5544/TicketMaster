using PaymentProvider.Models;

namespace PaymentProvider.Abstractions;

public interface IPaymentGatewayFactory
{
    IPaymentGateway Default { get; }

    IPaymentGateway Get(PaymentProviderKind kind);
}
