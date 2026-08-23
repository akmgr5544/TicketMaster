using ArchUnitNET.xUnit;
using MediatR;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace BookingArchitecture.Tests;

public class NamingConventionTest : BaseTest
{
    /// <summary>
    /// Reads are dispatched through the same pipeline as writes, so both suffixes are allowed — but a
    /// handler still has to declare which one it is.
    /// <para>
    /// Matched by pattern rather than with <c>HaveNameEndingWith</c> because reflected names carry the
    /// generic arity suffix: <c>IdentifiedCommandHandler</c> reports as
    /// <c>IdentifiedCommandHandler`2</c>, which no literal "ends with CommandHandler" check can ever
    /// satisfy. The trailing group is what admits that suffix without admitting anything else.
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
