using ArchUnitNET.Domain;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace EventsArchitecture.Tests;

public class DependenceTest : BaseTest
{
    private static readonly IObjectProvider<IType> Domain =
        Types().That().ResideInAssembly(DomainAssembly).As("Domain Layer");

    private static readonly IObjectProvider<IType> Application =
        Types().That().ResideInAssembly(ApplicationAssembly).As("Application Layer");

    private static readonly IObjectProvider<IType> Api =
        Types().That().ResideInAssembly(ApiAssembly).As("Api Layer");

    private static readonly IObjectProvider<IType> Cosmos =
        Types().That().ResideInAssembly(CosmosAssembly).As("Cosmos Layer");

    [Fact]
    public void DomainLayer_ShouldNotDependOn_ApplicationLayer()
    {
        Types().That().Are(Domain).Should()
            .NotDependOnAny(Application)
            .Check(Architecture);
    }

    [Fact]
    public void DomainLayer_ShouldNotDependOn_ApiLayer()
    {
        Types().That().Are(Domain).Should()
            .NotDependOnAny(Api)
            .Check(Architecture);
    }

    [Fact]
    public void DomainLayer_ShouldNotDependOn_CosmosLayer()
    {
        Types().That().Are(Domain).Should()
            .NotDependOnAny(Cosmos)
            .Check(Architecture);
    }

    [Fact]
    public void ApplicationLayer_ShouldNotDependOn_CosmosLayer()
    {
        Types().That().Are(Application).Should()
            .NotDependOnAny(Cosmos)
            .Check(Architecture);
    }

    [Fact]
    public void ApplicationLayer_ShouldNotDependOn_ApiLayer()
    {
        Types().That().Are(Application).Should()
            .NotDependOnAny(Api)
            .Check(Architecture);
    }

    [Fact]
    public void CosmosLayer_ShouldNotDependOn_ApiLayer()
    {
        Types().That().Are(Cosmos).Should()
            .NotDependOnAny(Api)
            .Check(Architecture);
    }

    /// <summary>
    /// The domain is persistence-ignorant, and these three rules are what keep it that way. Each
    /// one failed before the Cosmos migration: entity ids were MongoDB's ObjectId, Venue.Location
    /// was a System.Drawing.Point, and the domain referenced DI abstractions to provide an empty
    /// AddDomainServices. Swapping databases should be a mapping change, not a domain rewrite.
    /// </summary>
    [Fact]
    public void DomainLayer_ShouldNotDependOn_AnyDatabaseDriver()
    {
        Types().That().Are(Domain).Should()
            .NotDependOnAnyTypesThat().ResideInNamespaceMatching("^(Microsoft\\.Azure\\.Cosmos|MongoDB)")
            .Check(Architecture);
    }

    [Fact]
    public void DomainLayer_ShouldNotDependOn_SystemDrawing()
    {
        Types().That().Are(Domain).Should()
            .NotDependOnAnyTypesThat().ResideInNamespaceMatching("^System\\.Drawing")
            .Check(Architecture);
    }

    [Fact]
    public void DomainLayer_ShouldNotDependOn_DependencyInjectionAbstractions()
    {
        Types().That().Are(Domain).Should()
            .NotDependOnAnyTypesThat().ResideInNamespaceMatching("^Microsoft\\.Extensions")
            .Check(Architecture);
    }
}
