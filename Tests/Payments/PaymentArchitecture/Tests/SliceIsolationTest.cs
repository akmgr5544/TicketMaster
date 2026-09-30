using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace PaymentArchitecture.Tests;

// Slices of one aggregate share a namespace, so isolation is between areas, not between files. Code two
// areas need belongs in Shared (ProviderOutcome is the precedent).
public class SliceIsolationTest : BaseTest
{
    public static TheoryData<string> Areas => new() { "Checkouts", "PaymentOrders", "Webhooks", "Wallets" };

    [Theory]
    [MemberData(nameof(Areas))]
    public void FeatureArea_DoesNotDependOn_AnotherFeatureArea(string area)
    {
        Types().That().ResideInNamespace($"PaymentSystem.Features.{area}").Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching($@"^PaymentSystem\.Features\.(?!{area}$).+$")
            .Check(Architecture);
    }
}
