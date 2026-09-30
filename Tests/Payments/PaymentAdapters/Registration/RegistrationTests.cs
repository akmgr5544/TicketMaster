using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PaymentAdapters.Fixtures;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Extensions;
using PaymentProvider.Models;

namespace PaymentAdapters.Registration;

// Configuration mistakes must stop the host from starting. Found at the first payment instead, they
// fail a customer's checkout in production.
public sealed class RegistrationTests
{
    private const string Url = "http://127.0.0.1:1";

    [Fact]
    public void Resolves_each_configured_provider_by_kind()
    {
        var settings = Merge(Gateways.StripeSettings(Url), Gateways.BraintreeSettings(Url));
        settings["PaymentProviders:Default"] = "Braintree";

        var factory = Gateways.Build(settings).GetRequiredService<IPaymentGatewayFactory>();

        Assert.Equal(PaymentProviderKind.Braintree, factory.Default.Kind);
        Assert.Equal(PaymentProviderKind.Stripe, factory.Get(PaymentProviderKind.Stripe).Kind);
        Assert.Equal(PaymentProviderKind.Braintree, factory.Get(PaymentProviderKind.Braintree).Kind);
    }

    [Fact]
    public void Asking_for_a_provider_that_is_not_configured_is_a_configuration_error()
    {
        var factory = Gateways.Build(Gateways.StripeSettings(Url)).GetRequiredService<IPaymentGatewayFactory>();

        var exception = Assert.Throws<PaymentProviderException>(() => factory.Get(PaymentProviderKind.Braintree));

        Assert.Equal(PaymentProviderErrorKind.Configuration, exception.Kind);
    }

    [Fact]
    public void Registering_twice_does_not_break_resolution()
    {
        var provider = Gateways.Build(Gateways.StripeSettings(Url), services =>
            services.AddPaymentProviders(new ConfigurationBuilder()
                .AddInMemoryCollection(Gateways.StripeSettings(Url)).Build()));

        var factory = provider.GetRequiredService<IPaymentGatewayFactory>();

        Assert.Equal(PaymentProviderKind.Stripe, factory.Default.Kind);
    }

    [Fact]
    public void Gateways_are_shared_so_their_HTTP_connections_are_reused()
    {
        var provider = Gateways.Build(Gateways.StripeSettings(Url));

        Assert.Same(
            provider.GetRequiredService<IPaymentGatewayFactory>().Default,
            provider.CreateScope().ServiceProvider.GetRequiredService<IPaymentGatewayFactory>().Default);
    }

    [Fact]
    public void No_payment_configuration_at_all_fails_at_startup()
    {
        AssertStartupFails(new Dictionary<string, string?>());
    }

    [Fact]
    public void A_default_naming_an_unconfigured_provider_fails_at_startup()
    {
        var settings = Gateways.StripeSettings(Url);
        settings["PaymentProviders:Default"] = "Braintree";

        AssertStartupFails(settings);
    }

    [Fact]
    public void A_default_that_is_not_a_provider_at_all_fails_at_startup()
    {
        var settings = Gateways.StripeSettings(Url);
        settings["PaymentProviders:Default"] = "PayPal";

        AssertStartupFails(settings);
    }

    [Fact]
    public void Configuring_only_Braintree_without_naming_a_default_fails_at_startup_rather_than_silently_picking_Stripe()
    {
        var settings = Gateways.BraintreeSettings(Url);
        settings.Remove("PaymentProviders:Default");

        AssertStartupFails(settings);
    }

    [Theory]
    [InlineData("PaymentProviders:Stripe:SecretKey")]
    [InlineData("PaymentProviders:Stripe:WebhookSecret")]
    public void A_missing_Stripe_secret_fails_at_startup(string key)
    {
        var settings = Gateways.StripeSettings(Url);
        settings[key] = "";

        AssertStartupFails(settings);
    }

    [Theory]
    [InlineData("PaymentProviders:Braintree:MerchantId")]
    [InlineData("PaymentProviders:Braintree:PublicKey")]
    [InlineData("PaymentProviders:Braintree:PrivateKey")]
    public void A_missing_Braintree_credential_fails_at_startup(string key)
    {
        var settings = Gateways.BraintreeSettings(Url);
        settings[key] = "  ";

        AssertStartupFails(settings);
    }

    [Fact]
    public void Braintree_without_any_merchant_account_fails_at_startup()
    {
        var settings = Gateways.BraintreeSettings(Url);
        settings.Remove("PaymentProviders:Braintree:MerchantAccounts:EUR");
        settings.Remove("PaymentProviders:Braintree:MerchantAccounts:USD");

        AssertStartupFails(settings);
    }

    [Fact]
    public void A_Braintree_merchant_account_with_a_blank_id_fails_at_startup()
    {
        var settings = Gateways.BraintreeSettings(Url);
        settings["PaymentProviders:Braintree:MerchantAccounts:EUR"] = "";

        AssertStartupFails(settings);
    }

    [Fact]
    public void An_unknown_Braintree_environment_fails_at_startup()
    {
        var settings = Gateways.BraintreeSettings(Url);
        settings.Remove("PaymentProviders:Braintree:GatewayUrl");
        settings["PaymentProviders:Braintree:Environment"] = "prod";

        AssertStartupFails(settings);
    }

    [Theory]
    [InlineData("sandbox")]
    [InlineData("Sandbox")]
    [InlineData("production")]
    [InlineData("Production")]
    public void A_real_Braintree_environment_starts(string environment)
    {
        var settings = Gateways.BraintreeSettings(Url);
        settings.Remove("PaymentProviders:Braintree:GatewayUrl");
        settings["PaymentProviders:Braintree:Environment"] = environment;

        var factory = Gateways.Build(settings).GetRequiredService<IPaymentGatewayFactory>();

        Assert.Equal(PaymentProviderKind.Braintree, factory.Default.Kind);
    }

    [Fact]
    public void A_misconfigured_provider_that_is_not_the_default_still_fails_at_startup()
    {
        var settings = Merge(Gateways.StripeSettings(Url), Gateways.BraintreeSettings(Url));
        settings["PaymentProviders:Default"] = "Stripe";
        settings["PaymentProviders:Braintree:PrivateKey"] = "";

        AssertStartupFails(settings);
    }

    private static void AssertStartupFails(Dictionary<string, string?> settings)
    {
        // Only what a host does at boot: build the container and run the startup validators. Nothing
        // is resolved, so a mistake that surfaces only when a gateway is first built counts as a miss.
        var exception = Record.Exception(() => Gateways.Build(settings));

        Assert.NotNull(exception);
        Assert.IsNotType<NullReferenceException>(exception);
    }

    private static Dictionary<string, string?> Merge(params Dictionary<string, string?>[] parts) =>
        parts.SelectMany(part => part).GroupBy(pair => pair.Key).ToDictionary(group => group.Key, group => group.Last().Value);
}
