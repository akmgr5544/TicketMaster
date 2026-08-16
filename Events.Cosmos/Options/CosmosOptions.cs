namespace Events.Cosmos.Options;

public class CosmosOptions
{
    public const string SectionName = "CosmosConfigs";

    public string ConnectionString { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;

    /// <summary>
    /// Provisioned at the database level so the three containers share one 400 RU/s floor rather
    /// than paying a minimum each.
    /// </summary>
    public int Throughput { get; set; } = 400;
}
