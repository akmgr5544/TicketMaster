namespace Events.Cosmos;

/// <summary>
/// One container per aggregate root, each partitioned by its own id.
/// <para>
/// <b>The partition key is immutable.</b> Cosmos cannot change a container's partition key in
/// place — switching to a different one means copying every item into a new container. It is
/// <c>/id</c> here because this catalogue is read almost entirely by id, and a point read on
/// id + partition key is the cheapest operation Cosmos offers. Queries filtering on anything else
/// fan out across partitions, which is affordable at catalogue size and would not be at scale.
/// </para>
/// </summary>
public static class CosmosContainers
{
    public const string PartitionKeyPath = "/id";

    public const string Events = "events";
    public const string Venues = "venues";
    public const string Performers = "performers";

    public static readonly IReadOnlyList<string> All = [Events, Venues, Performers];
}
