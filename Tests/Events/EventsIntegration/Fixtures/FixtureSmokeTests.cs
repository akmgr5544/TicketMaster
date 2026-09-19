using Events.Cosmos;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;

namespace EventsIntegration.Fixtures;

// Proves the fixture stands up against a live emulator at all: the container booted, the connection
// mode is one the emulator accepts, and provisioning ran. If this is red, every other test is red for
// the same reason, so it is the first thing to read.
public sealed class FixtureSmokeTests : EventsIntegrationTest
{
    private readonly EventsFixture _fixture;

    public FixtureSmokeTests(EventsFixture fixture) : base(fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Provisions_all_three_containers()
    {
        var client = _fixture.Services.GetRequiredService<CosmosClient>();
        var database = client.GetDatabase("events_test");

        foreach (var name in CosmosContainers.All)
        {
            // Reading properties throws NotFound if the container was never created — a real check
            // that provisioning ran, not just that GetContainer returns a proxy.
            var properties = await database.GetContainer(name).ReadContainerAsync();

            Assert.Equal(name, properties.Resource.Id);
            Assert.Equal(CosmosContainers.PartitionKeyPath, properties.Resource.PartitionKeyPath);
        }
    }
}
