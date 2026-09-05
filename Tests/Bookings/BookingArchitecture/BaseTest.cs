using System.Reflection;
using ArchUnitNET.Loader;
using Bookings.Api;
using Bookings.Application;
using Bookings.Domain;
using Bookings.Sql;
using ArchUnitNET.Domain;
using Assembly = System.Reflection.Assembly;

namespace BookingArchitecture;

public abstract class BaseTest
{
    protected static readonly Assembly ApiAssembly = typeof(IApiAssemblyMarker).Assembly;
    protected static readonly Assembly ApplicationAssembly = typeof(IApplicationAssemblyMarker).Assembly;
    protected static readonly Assembly DomainAssembly = typeof(IDomainAssemblyMarker).GetTypeInfo().Assembly;
    protected static readonly Assembly SqlAssembly = typeof(ISqlAssemblyMarker).Assembly;

    protected static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(ApiAssembly,
            ApplicationAssembly,
            DomainAssembly,
            SqlAssembly)
        .Build();
}