using ArchUnitNET.Domain;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace BookingArchitecture.Tests;

public class DependenceTest : BaseTest
{
    private static readonly IObjectProvider<IType> Domain =
        Types().That().ResideInAssembly(DomainAssembly).As("Domain Layer");

    private static readonly IObjectProvider<IType> Application =
        Types().That().ResideInAssembly(ApplicationAssembly).As("Application Layer");

    private static readonly IObjectProvider<IType> Api =
        Types().That().ResideInAssembly(ApiAssembly).As("Api Layer");

    private static readonly IObjectProvider<IType> Sql =
        Types().That().ResideInAssembly(SqlAssembly).As("Sql Layer");

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
    public void DomainLayer_ShouldNotDependOn_SqlLayer()
    {
        Types().That().Are(Domain).Should()
            .NotDependOnAny(Sql)
            .Check(Architecture);
    }
    
    [Fact]
    public void ApplicationLayer_ShouldNotDependOn_SqlLayer()
    {
        Types().That().Are(Application).Should()
            .NotDependOnAny(Sql)
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
    public void SqlLayer_ShouldNotDependOn_ApiLayer()
    {
        Types().That().Are(Sql).Should()
            .NotDependOnAny(Api)
            .Check(Architecture);
    }
}