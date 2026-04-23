using ArchUnitNET.xUnit;
using MediatR;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace BookingArchitecture.Tests;

public class VisibilityTest : BaseTest
{
    [Fact]
    public void CommandHandlers_ShouldNotBePublic()
    {
        Classes().That().ResideInAssembly(ApplicationAssembly)
            .And()
            .ImplementInterface(typeof(IRequestHandler<>))
            .Or()
            .ImplementInterface(typeof(IRequestHandler<,>))
            .Should().BeInternal()
            .Check(Architecture);
    }
}