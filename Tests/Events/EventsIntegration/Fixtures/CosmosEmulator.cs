using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace EventsIntegration.Fixtures;

// One definition of how to run the emulator, shared by the fast fixture and the host fixture so the
// image tag, port, readiness signal and the well-known key cannot drift between them.
public static class CosmosEmulator
{
    // Microsoft's well-known emulator account key — not a secret; the same one appsettings.Development
    // and compose.yaml carry.
    public const string Key =
        "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    // vnext-latest is the only line with a native arm64 build (Apple Silicon); the classic `latest`
    // emulator is amd64-only. It serves cleartext http on 8081 and rejects the SDK's default Direct
    // mode — hence the http endpoint and Gateway mode at the call sites.
    public static IContainer NewContainer() =>
        new ContainerBuilder("mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-latest")
            .WithPortBinding(8081, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("fully ready to accept requests"))
            .Build();

    public static string ConnectionString(IContainer container) =>
        $"AccountEndpoint=http://localhost:{container.GetMappedPublicPort(8081)}/;AccountKey={Key}";
}
