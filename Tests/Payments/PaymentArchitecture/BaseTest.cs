using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using PaymentProvider.Abstractions;
using PaymentSystem.Data;
using Assembly = System.Reflection.Assembly;

namespace PaymentArchitecture;

public abstract class BaseTest
{
    protected static readonly Assembly PaymentSystemAssembly = typeof(PaymentDbContext).Assembly;
    protected static readonly Assembly PaymentProviderAssembly = typeof(IPaymentGateway).Assembly;

    protected static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(PaymentSystemAssembly, PaymentProviderAssembly)
        .Build();
}
