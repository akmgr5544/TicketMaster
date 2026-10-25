using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace PaymentArchitecture.Tests;

public class LayoutTest : BaseTest
{
    // One folder per area, one file per feature, no per-feature folder: a feature's types sit directly in its area's
    // namespace, which mirrors the folder.
    [Fact]
    public void FeatureTypes_LiveInTheirAreaNamespace()
    {
        Types().That().ResideInNamespaceMatching(@"^PaymentSystem\.Features(\..+)?$")
            .Should().ResideInNamespaceMatching(@"^PaymentSystem\.Features\.[^.]+$")
            .Check(Architecture);
    }

    // ADR-005: ISender is the narrow surface a Mediator swap has to replace; IMediator would widen it. Checked over the
    // whole of Features so the lambdas endpoints compile into nested types are covered too. Matched by name: a type
    // nothing references is not in the loaded architecture, and typeof(IMediator) would throw rather than pass.
    [Fact]
    public void Features_DoNotDependOn_IMediator()
    {
        Types().That().ResideInNamespaceMatching(@"^PaymentSystem\.Features(\..+)?$")
            .Should().NotDependOnAnyTypesThat().HaveFullNameMatching(@"^MediatR\.IMediator$")
            .Check(Architecture);
    }
}
