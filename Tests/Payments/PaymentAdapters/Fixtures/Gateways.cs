using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PaymentProvider.Abstractions;
using PaymentProvider.Extensions;
using PaymentProvider.Models;

namespace PaymentAdapters.Fixtures;

// Every gateway under test is built by the production AddPaymentProviders, never by hand, and the
// startup validation a host would run is run here too.
public static class Gateways
{
    public const string StripeSecretKey = "sk_test_fake";
    public const string StripeWebhookSecret = "whsec_test_secret";
    public const string BraintreeMerchantId = "merchant_1";
    public const string BraintreePublicKey = "public_key_1";
    public const string BraintreePrivateKey = "private_key_1";
    public const string EurMerchantAccount = "acct_eur";
    public const string UsdMerchantAccount = "acct_usd";

    public static Dictionary<string, string?> StripeSettings(string apiBase) => new()
    {
        ["PaymentProviders:Default"] = "Stripe",
        ["PaymentProviders:Stripe:SecretKey"] = StripeSecretKey,
        ["PaymentProviders:Stripe:WebhookSecret"] = StripeWebhookSecret,
        ["PaymentProviders:Stripe:ApiBase"] = apiBase,
    };

    public static Dictionary<string, string?> BraintreeSettings(string gatewayUrl) => new()
    {
        ["PaymentProviders:Default"] = "Braintree",
        ["PaymentProviders:Braintree:MerchantId"] = BraintreeMerchantId,
        ["PaymentProviders:Braintree:PublicKey"] = BraintreePublicKey,
        ["PaymentProviders:Braintree:PrivateKey"] = BraintreePrivateKey,
        ["PaymentProviders:Braintree:GatewayUrl"] = gatewayUrl,
        ["PaymentProviders:Braintree:MerchantAccounts:EUR"] = EurMerchantAccount,
        ["PaymentProviders:Braintree:MerchantAccounts:USD"] = UsdMerchantAccount,
    };

    public static ServiceProvider Build(IDictionary<string, string?> settings, Action<IServiceCollection>? extra = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddPaymentProviders(configuration);
        extra?.Invoke(services);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        provider.GetRequiredService<IStartupValidator>().Validate();
        return provider;
    }

    public static IPaymentGateway Stripe(FakeHttpServer server) =>
        Build(StripeSettings(server.BaseUrl)).GetRequiredService<IPaymentGatewayFactory>().Get(PaymentProviderKind.Stripe);

    public static IPaymentGateway Braintree(FakeHttpServer server) =>
        Build(BraintreeSettings(server.BaseUrl)).GetRequiredService<IPaymentGatewayFactory>().Get(PaymentProviderKind.Braintree);
}
