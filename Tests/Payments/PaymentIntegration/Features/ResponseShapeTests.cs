using PaymentIntegration.Fixtures;
using System.Reflection;
using PaymentSystem.Domain;
using PaymentSystem.Features.PaymentOrders;
using PaymentSystem.Features.Checkouts;
using PaymentSystem.Features.Wallets;

namespace PaymentIntegration.Features;

// No database: these assert on the response types themselves, walked recursively through nested records
// and collection element types, so a token or an entity added three levels down is still caught.
public sealed class ResponseShapeTests
{
    public static TheoryData<Type> Responses =>
    [
        typeof(GetCheckout.Response),
        typeof(GetPaymentOrder.Response),
        typeof(GetMyWallets.Response),
        typeof(GetOrderLedger.Response)
    ];

    [Theory]
    [MemberData(nameof(Responses))]
    public void NoResponse_CarriesThePspToken(Type response)
    {
        var offending = Reachable(response)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(p => p.Name.Contains("Psp", StringComparison.OrdinalIgnoreCase)
                        || p.Name.Contains("Token", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.DeclaringType!.Name}.{p.Name}")
            .ToList();

        Assert.Empty(offending);
    }

    [Theory]
    [MemberData(nameof(Responses))]
    public void NoResponse_ExposesADomainType(Type response)
    {
        var domainAssembly = typeof(PaymentEvent).Assembly;
        var leaked = Reachable(response)
            .Where(t => t.Namespace?.StartsWith("PaymentSystem.Domain", StringComparison.Ordinal) == true
                        && t.Assembly == domainAssembly)
            .ToList();

        Assert.Empty(leaked);
    }

    private static IEnumerable<Type> Reachable(Type root)
    {
        var seen = new HashSet<Type>();
        var pending = new Stack<Type>([root]);
        while (pending.TryPop(out var type))
        {
            if (!seen.Add(type))
                continue;
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var propertyType = property.PropertyType;
                if (propertyType.IsGenericType)
                    foreach (var argument in propertyType.GetGenericArguments())
                        pending.Push(argument);
                if (propertyType.Assembly == root.Assembly || propertyType.Namespace?.StartsWith("PaymentSystem") == true)
                    pending.Push(propertyType);
            }
        }
        return seen;
    }
}
