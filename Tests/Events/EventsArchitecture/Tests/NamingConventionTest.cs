using ArchUnitNET.xUnit;
using MediatR;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace EventsArchitecture.Tests;

public class NamingConventionTest : BaseTest
{
    /// <summary>
    /// Reads are dispatched through the same pipeline as writes, so both suffixes are allowed — but a
    /// handler still has to declare which one it is.
    /// <para>
    /// Matched by pattern rather than with <c>HaveNameEndingWith</c> because reflected names carry the
    /// generic arity suffix: a generic handler reports as <c>SomethingHandler`2</c>, which no literal
    /// "ends with CommandHandler" check can ever satisfy. Events has no generic handler today, so the
    /// literal form passed by luck rather than by being right.
    /// </para>
    /// </summary>
    [Fact]
    public void Handlers_ShouldHave_NameEndingWith_CommandHandler_Or_QueryHandler()
    {
        Classes().That().ResideInAssembly(ApplicationAssembly)
            .And()
            .ImplementInterface(typeof(IRequestHandler<>))
            .Or()
            .ImplementInterface(typeof(IRequestHandler<,>))
            .Should().HaveNameMatching(@"(Command|Query)Handler(`\d+)?$")
            .Check(Architecture);
    }
}
