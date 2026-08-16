using ArchUnitNET.xUnit;
using MediatR;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace EventsArchitecture.Tests;

public class NamingConventionTest : BaseTest
{
    [Fact]
    public void CommandHandlers_ShouldHave_NameEndingWith_CommandHandler()
    {
        Classes().That().ResideInAssembly(ApplicationAssembly)
            .And()
            .ImplementInterface(typeof(IRequestHandler<>))
            .Or()
            .ImplementInterface(typeof(IRequestHandler<,>))
            .Should().HaveNameEndingWith("CommandHandler")
            .Check(Architecture);
    }
}