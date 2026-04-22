using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using Users.Api;
using Assembly = System.Reflection.Assembly;

namespace ArchitectureTests.BaseTests;

public class UsersBaseTest
{
    protected static readonly Assembly UsersAssembly = typeof(IAssemblyMarker).Assembly;

    protected static readonly Architecture Architecture = new ArchLoader()
        .LoadAssemblies(UsersAssembly)
        .Build();


}