using ArchitectureTests.BaseTests;
using ArchUnitNET.Domain;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace ArchitectureTests.DependenceTests.Events;

public class DependenceTest : EventsBaseTest
{
    private static readonly IObjectProvider<IType> Domain =
        Types().That().ResideInAssembly(DomainAssembly).As("Domain Layer");

    private static readonly IObjectProvider<IType> Application =
        Types().That().ResideInAssembly(ApplicationAssembly).As("Application Layer");

    private static readonly IObjectProvider<IType> Api =
        Types().That().ResideInAssembly(ApiAssembly).As("Api Layer");

    private static readonly IObjectProvider<IType> Mongo =
        Types().That().ResideInAssembly(MongoAssembly).As("Mongo Layer");
    
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
    public void DomainLayer_ShouldNotDependOn_MongoLayer()
    {
        Types().That().Are(Domain).Should()
            .NotDependOnAny(Mongo)
            .Check(Architecture);
    }
    
    [Fact]
    public void ApplicationLayer_ShouldNotDependOn_MongoLayer()
    {
        Types().That().Are(Application).Should()
            .NotDependOnAny(Mongo)
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
    public void MongoLayer_ShouldNotDependOn_ApiLayer()
    {
        Types().That().Are(Mongo).Should()
            .NotDependOnAny(Api)
            .Check(Architecture);
    }
}