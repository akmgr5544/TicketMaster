using ArchUnitNET.Domain;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace PaymentArchitecture.Tests;

public class DependenceTest : BaseTest
{
    private static readonly IObjectProvider<IType> Domain =
        Types().That().ResideInNamespaceMatching(@"^PaymentSystem\.Domain(\..+)?$").As("Domain");

    private static readonly IObjectProvider<IType> Query =
        Types().That().ResideInNamespaceMatching(@"^PaymentSystem\.Query(\..+)?$").As("Query");

    private static readonly IObjectProvider<IType> Data =
        Types().That().ResideInNamespaceMatching(@"^PaymentSystem\.Data(\..+)?$").As("Data");

    private static readonly IObjectProvider<IType> Shared =
        Types().That().ResideInNamespaceMatching(@"^PaymentSystem\.Shared(\..+)?$").As("Shared");

    private static readonly IObjectProvider<IType> Features =
        Types().That().ResideInNamespaceMatching(@"^PaymentSystem\.Features(\..+)?$").As("Features");

    [Fact]
    public void Domain_DoesNotDependOn_DataFeaturesSharedQueryOrExtensions()
    {
        Types().That().Are(Domain).Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"^PaymentSystem\.(Data|Features|Shared|Query|Extensions)(\..+)?$")
            .Check(Architecture);
    }

    [Fact]
    public void Domain_DoesNotDependOn_PersistenceMessagingWebOrPsp()
    {
        Types().That().Are(Domain).Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(
                @"^(Microsoft\.EntityFrameworkCore|Npgsql|Wolverine|JasperFx|Microsoft\.AspNetCore|PaymentProvider)(\..+)?$")
            .Check(Architecture);
    }

    // The allowlist catches a library the deny-list above never thought of. MediatR is allowed only because
    // DomainEvent is an INotification. Written as "nothing outside" rather than OnlyDependOnTypesThat, which ignores
    // types from assemblies the architecture did not load and so passes whatever the domain uses.
    [Fact]
    public void Domain_DependsOnlyOn_ItselfTheEnumsTheBclAndMediatR()
    {
        Types().That().Are(Domain).Should()
            .NotDependOnAnyTypesThat()
            .DoNotResideInNamespaceMatching(@"^(PaymentSystem\.Domain|PaymentSystem\.Enums|System|MediatR)(\..+)?$")
            .Check(Architecture);
    }

    [Fact]
    public void PaymentProvider_DoesNotDependOn_PaymentSystem()
    {
        Types().That().ResideInAssembly(PaymentProviderAssembly).Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"^PaymentSystem(\..+)?$")
            .Check(Architecture);
    }

    // Read models are plain data over the same tables; tying them to the aggregates would drag the domain's rules
    // into every read.
    [Fact]
    public void ReadModels_DoNotDependOn_Domain()
    {
        Types().That().Are(Query).Should()
            .NotDependOnAny(Domain)
            .Check(Architecture);
    }

    [Fact]
    public void Shared_DoesNotDependOn_Features()
    {
        Types().That().Are(Shared).Should()
            .NotDependOnAny(Features)
            .Check(Architecture);
    }

    [Fact]
    public void Data_DoesNotDependOn_Features()
    {
        Types().That().Are(Data).Should()
            .NotDependOnAny(Features)
            .Check(Architecture);
    }
}
