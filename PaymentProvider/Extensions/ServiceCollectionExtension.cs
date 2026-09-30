using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PaymentProvider.Abstractions;
using PaymentProvider.Configuration;
using PaymentProvider.Providers;
using PaymentProvider.Providers.Braintree;
using PaymentProvider.Providers.Stripe;

namespace PaymentProvider.Extensions;

public static class ServiceCollectionExtension
{

    // Registers every provider that has a section under "PaymentProviders" (e.g. PaymentProviders:Stripe).
    // Options are validated at startup, so a missing secret fails the boot rather than the first payment.
    // TryAdd throughout: a second call must not register a provider twice, which the factory refuses.
    public static IServiceCollection AddPaymentProviders(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(PaymentProvidersOptions.SectionName);

        var stripe = section.GetSection("Stripe");
        if (stripe.Exists())
        {
            services.AddOptions<StripeOptions>()
                .Bind(stripe)
                .Validate(
                    options => !string.IsNullOrWhiteSpace(options.SecretKey)
                        && !string.IsNullOrWhiteSpace(options.WebhookSecret),
                    "Stripe requires SecretKey and WebhookSecret.")
                .ValidateOnStart();

            services.TryAddSingleton<IStripeApi, StripeApi>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IPaymentGateway, StripePaymentGateway>());
        }

        var braintree = section.GetSection("Braintree");
        if (braintree.Exists())
        {
            services.AddOptions<BraintreeOptions>()
                .Bind(braintree)
                .Validate(
                    options => !string.IsNullOrWhiteSpace(options.MerchantId)
                        && !string.IsNullOrWhiteSpace(options.PublicKey)
                        && !string.IsNullOrWhiteSpace(options.PrivateKey),
                    "Braintree requires MerchantId, PublicKey and PrivateKey.")
                .Validate(
                    options => options.MerchantAccounts.Count > 0
                        && options.MerchantAccounts.All(account =>
                            account.Key.Length == 3 && account.Key.All(char.IsAsciiLetter)
                            && !string.IsNullOrWhiteSpace(account.Value)),
                    "Braintree requires at least one MerchantAccounts entry, each an ISO 4217 code mapped to an account id.")
                .Validate(
                    options => options.GatewayUrl is not null || IsBraintreeEnvironment(options.Environment),
                    "Braintree Environment is not one Braintree recognises (e.g. sandbox, production).")
                .ValidateOnStart();

            services.TryAddSingleton<IBraintreeApi, BraintreeApi>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IPaymentGateway, BraintreePaymentGateway>());
        }

        services.AddOptions<PaymentProvidersOptions>()
            .Bind(section)
            .Validate(
                options => section.GetSection(options.Default.ToString()).Exists(),
                $"{PaymentProvidersOptions.SectionName}:Default names a provider that has no configuration section.")
            .ValidateOnStart();

        services.TryAddSingleton<IPaymentGatewayFactory, PaymentGatewayFactory>();
        return services;
    }

    // Asks the SDK itself, which is the only authority on the names it accepts. An unknown one would
    // otherwise throw only when the gateway is first built, which is the first payment.
    private static bool IsBraintreeEnvironment(string environment)
    {
        try
        {
            global::Braintree.Environment.ParseEnvironment(environment);
            return true;
        }
        catch (global::Braintree.Exceptions.ConfigurationException)
        {
            return false;
        }
    }
}
