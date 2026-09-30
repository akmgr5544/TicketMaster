using ArchUnitNET.Domain;
using ArchUnitNET.xUnit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace PaymentArchitecture.Tests;

public class DependenceTest : BaseTest
{
    private static readonly IObjectProvider<IType> Domain =
        Types().That().ResideInNamespaceMatching(@"^PaymentSystem\.Domain(\..+)?$").As("Domain");

    private static readonly IObjectProvider<IType> Data =
        Types().That().ResideInNamespaceMatching(@"^PaymentSystem\.Data(\..+)?$").As("Data");

    private static readonly IObjectProvider<IType> Shared =
        Types().That().ResideInNamespaceMatching(@"^PaymentSystem\.Shared(\..+)?$").As("Shared");

    private static readonly IObjectProvider<IType> Features =
        Types().That().ResideInNamespaceMatching(@"^PaymentSystem\.Features(\..+)?$").As("Features");

    [Fact]
    public void Domain_DoesNotDependOn_DataFeaturesSharedOrExtensions()
    {
        Types().That().Are(Domain).Should()
            .NotDependOnAnyTypesThat()
            .ResideInNamespaceMatching(@"^PaymentSystem\.(Data|Features|Shared|Extensions)(\..+)?$")
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

    // The allowlist catches a library the deny-list above never thought of. MediatR is in it only because
    // DomainEvent is an INotification, the same trade-off Bookings makes; PaymentSystem.Enums holds the
    // order status and entry type the aggregates use.
    // Written as "no dependency outside" rather than OnlyDependOnTypesThat, which in ArchUnitNET 0.13.3
    // ignores types from assemblies the architecture did not load and so passes whatever the domain uses.
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
