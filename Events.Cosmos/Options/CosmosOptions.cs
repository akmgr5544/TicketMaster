using Microsoft.Azure.Cosmos;

namespace Events.Cosmos.Options;

public class CosmosOptions
{
    public const string SectionName = "CosmosConfigs";

    public string ConnectionString { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;

    /// <summary>
    /// Left unset in every real deployment, which leaves the SDK on its default (Direct) mode. It
    /// exists for the integration emulator, which serves cleartext http and only accepts Gateway
    /// mode — setting it there lets the fixture use the real client construction rather than a
    /// hand-copied one. When set, the client is also pinned to the single endpoint.
    /// </summary>
    public ConnectionMode? ConnectionMode { get; set; }

    /// <summary>
    /// Provisioned at the database level so the three containers share one 400 RU/s floor rather
    /// than paying a minimum each.
    /// </summary>
    public int Throughput { get; set; } = 400;
}
