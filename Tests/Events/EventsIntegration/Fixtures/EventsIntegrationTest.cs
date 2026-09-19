using Events.Domain.Repositories;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace EventsIntegration.Fixtures;

[Collection(EventsCollection.Name)]
public abstract class EventsIntegrationTest : IAsyncLifetime
{
    private readonly EventsFixture _fixture;
    private AsyncServiceScope _act;

    protected EventsIntegrationTest(EventsFixture fixture)
    {
        _fixture = fixture;
    }

    protected IServiceProvider Act => _act.ServiceProvider;

    protected ISender Sender => Act.GetRequiredService<ISender>();

    protected IEventRepository Events => Act.GetRequiredService<IEventRepository>();
    protected IVenueRepository Venues => Act.GetRequiredService<IVenueRepository>();
    protected IPerformerRepository Performers => Act.GetRequiredService<IPerformerRepository>();

    protected Seed Seed { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _fixture.ResetAsync();
        _act = _fixture.Services.CreateAsyncScope();
        Seed = new Seed(_fixture.Services);
    }

    public async Task DisposeAsync()
    {
        await _act.DisposeAsync();
    }

    /// <summary>
    /// Runs work in a scope of its own — a fresh repository with a fresh ETag cache. Reading back a
    /// write through the same repository that made it would hit that scope's cached ETag and prove
    /// nothing about a competing writer; a real 412 needs two scopes. Also how a test loads a row to
    /// assert on what actually reached the store.
    /// </summary>
    protected async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }
}
