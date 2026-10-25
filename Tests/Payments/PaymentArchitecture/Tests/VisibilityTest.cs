using ArchUnitNET.xUnit;
using MediatR;
using PaymentSystem.Shared.Endpoints;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace PaymentArchitecture.Tests;

public class VisibilityTest : BaseTest
{
    // ADR-005: internal sealed handlers keep a later swap from MediatR to Mediator cheap.
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

    // Scrutor discovers endpoints by scanning public types for IEndpointMarker, so one that is not public is silently
    // never mapped.
    [Fact]
    public void Endpoints_ArePublicSealed_AndLiveInFeatures()
    {
        Classes().That().ImplementInterface(typeof(IEndpointMarker))
            .Should().BePublic()
            .AndShould().BeSealed()
            .AndShould().ResideInNamespaceMatching(@"^PaymentSystem\.Features\..+$")
            .Check(Architecture);
    }

    // Wolverine discovers message handlers among public types; an internal one never receives its message.
    [Fact]
    public void WolverineHandlers_ArePublic()
    {
        Classes().That().ResideInNamespaceMatching(@"^PaymentSystem\.Features\..+$")
            .And().HaveNameEndingWith("Handler")
            .And().DoNotImplementInterface(typeof(IRequestHandler<>))
            .And().DoNotImplementInterface(typeof(IRequestHandler<,>))
            .And().DoNotImplementInterface(typeof(INotificationHandler<>))
            .Should().BePublic()
            .Check(Architecture);
    }
}
