using System.Reflection;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using Events.Api;
using Events.Application;
using Events.Cosmos;
using Events.Domain;
using Assembly = System.Reflection.Assembly;

namespace EventsArchitecture;

public abstract class BaseTest
{
    protected static readonly Assembly ApiAssembly = typeof(IApiAssemblyMarker).Assembly;
    protected static readonly Assembly ApplicationAssembly = typeof(IApplicationAssemblyMarker).Assembly;
    protected static readonly Assembly DomainAssembly = typeof(IDomainAssemblyMarker).GetTypeInfo().Assembly;
    protected static readonly Assembly CosmosAssembly = typeof(ICosmosAssemblyMarker).Assembly;

    protected static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(ApiAssembly,
            ApplicationAssembly,
            DomainAssembly,
            CosmosAssembly)
        .Build();
}
