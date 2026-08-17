using ArchUnitNET.xUnit;
using MediatR;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace EventsArchitecture.Tests;

public class NamingConventionTest : BaseTest
{
    /// <summary>
    /// Reads are dispatched through the same pipeline as writes, so both suffixes are allowed —
    /// but a handler still has to declare which one it is.
    /// </summary>
    [Fact]
    public void Handlers_ShouldHave_NameEndingWith_CommandHandler_Or_QueryHandler()
    {
        Classes().That().ResideInAssembly(ApplicationAssembly)
            .And()
            .ImplementInterface(typeof(IRequestHandler<>))
            .Or()
            .ImplementInterface(typeof(IRequestHandler<,>))
            .Should().HaveNameEndingWith("CommandHandler")
            .OrShould().HaveNameEndingWith("QueryHandler")
            .Check(Architecture);
    }
}
