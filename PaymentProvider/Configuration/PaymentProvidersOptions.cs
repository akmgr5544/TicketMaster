using PaymentProvider.Models;

namespace PaymentProvider.Configuration;

internal sealed class PaymentProvidersOptions
{
    public const string SectionName = "PaymentProviders";

    public PaymentProviderKind Default { get; set; }
}
