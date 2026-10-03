using System.Collections.Frozen;
using Microsoft.Extensions.Options;
using PaymentProvider.Abstractions;
using PaymentProvider.Configuration;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;

namespace PaymentProvider.Providers;

internal sealed class PaymentGatewayFactory : IPaymentGatewayFactory
{
    private readonly FrozenDictionary<PaymentProviderKind, IPaymentGateway> _gateways;

    public PaymentGatewayFactory(IEnumerable<IPaymentGateway> gateways, IOptions<PaymentProvidersOptions> options)
    {
        _gateways = gateways.ToFrozenDictionary(gateway => gateway.Kind);
        Default = Get(options.Value.Default);
    }

    public IPaymentGateway Default { get; }

    public IPaymentGateway Get(PaymentProviderKind kind) =>
        _gateways.TryGetValue(kind, out var gateway)
            ? gateway
            : throw new PaymentProviderException(
                kind, PaymentProviderErrorKind.Configuration, $"The {kind} payment provider is not configured.");

    public IPaymentGateway ForProvider(string? provider) =>
        provider is null ? Default : Get(Enum.Parse<PaymentProviderKind>(provider));
}
