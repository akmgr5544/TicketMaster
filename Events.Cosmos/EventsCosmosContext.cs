using Events.Cosmos.Options;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace Events.Cosmos;

public class EventsCosmosContext
{
    public EventsCosmosContext(CosmosClient client, IOptions<CosmosOptions> options)
    {
        var database = client.GetDatabase(options.Value.Database);

        Events = database.GetContainer(CosmosContainers.Events);
        Venues = database.GetContainer(CosmosContainers.Venues);
        Performers = database.GetContainer(CosmosContainers.Performers);
    }

    public Container Events { get; }
    public Container Venues { get; }
    public Container Performers { get; }
}
