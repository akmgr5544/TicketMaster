using ArchitectureTests.BaseTests;
using ArchUnitNET.xUnit;
using MediatR;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace ArchitectureTests.DependenceTests.Events;

public class VisibilityTest : EventsBaseTest
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