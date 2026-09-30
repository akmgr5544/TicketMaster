using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace PaymentArchitecture.Tests;

public class LayoutTest : BaseTest
{
    // One file per feature, no per-feature folder: every type under Features — nested ones by their
    // declaring type — carries its aggregate folder's namespace and nothing deeper.
    [Fact]
    public void FeatureTypes_ResideIn_TheirAggregateNamespace()
    {
        Types().That().ResideInNamespaceMatching(@"^PaymentSystem\.Features(\..+)?$").Should()
            .ResideInNamespaceMatching(@"^PaymentSystem\.Features\.(Checkouts|PaymentOrders|Webhooks|Wallets)$")
            .Check(Architecture);
    }
}
