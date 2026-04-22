using ArchitectureTests.BaseTests;
using MediatR;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace ArchitectureTests.DependenceTests.Booking;

public class NamingConventionTest : BookingBaseTest
{
    [Fact]
    public void CommandHandlers_ShouldHave_NameEndingWith_CommandHandler()
    {
        Classes().That().ResideInAssembly(ApplicationAssembly)
            .And()
            .ImplementInterface(typeof(IRequestHandler<>))
            .Or()
            .ImplementInterface(typeof(IRequestHandler<,>))
            .Should().HaveNameEndingWith("CommandHandler");
    }
}