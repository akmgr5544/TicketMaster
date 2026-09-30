using ArchUnitNET.xUnit;
using MediatR;
using PaymentSystem.Shared.Endpoints;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace PaymentArchitecture.Tests;

public class VisibilityTest : BaseTest
{
    [Fact]
    public void MediatRHandlers_AreNotPublic_AndAreSealed()
    {
        Classes().That().ImplementInterface(typeof(IRequestHandler<>))
            .Or().ImplementInterface(typeof(IRequestHandler<,>))
            .Or().ImplementInterface(typeof(INotificationHandler<>))
            .Should().NotBePublic()
            .AndShould().BeSealed()
            .Check(Architecture);
    }

    // Scrutor discovers endpoints by scanning public types for IEndpointMarker, so one that is not public
    // is silently never mapped.
    [Fact]
    public void Endpoints_ArePublicSealed_AndLiveInFeatures()
    {
        Classes().That().ImplementInterface(typeof(IEndpointMarker))
            .Should().BePublic()
            .AndShould().BeSealed()
            .AndShould().ResideInNamespaceMatching(@"^PaymentSystem\.Features\..+$")
            .Check(Architecture);
    }
}
