using Events.Domain.Entities;
using Events.Domain.Repositories;
using Events.Domain.ValueObjects;
using Microsoft.Extensions.DependencyInjection;

namespace EventsIntegration.Fixtures;

// Arrange by persisting aggregates directly, not by driving the handler under test. Each call uses
// its own scope so it never shares a repository's ETag cache with the acting scope — seeding must not
// prime the very cache a 412 test depends on being empty.
public sealed class Seed
{
    private readonly IServiceProvider _services;

    public Seed(IServiceProvider services)
    {
        _services = services;
    }

    // Ten days is Event.MinimumLeadTime; a fortnight clears it comfortably.
    private static DateTime SoonEnough => DateTime.UtcNow.AddDays(14);

    public async Task<Venue> VenueAsync(string name = "Arena", IEnumerable<string>? seats = null)
    {
        var venue = new Venue(name, "1 Main St", new GeoLocation(40, -70), seats ?? ["A1", "A2"]);
        await InScopeAsync((IVenueRepository repo, CancellationToken ct) => repo.AddVenueAsync(venue, ct));
        return venue;
    }

    public async Task<Performer> PerformerAsync(string name = "The Band")
    {
        var performer = new Performer(name, "headliner");
        await InScopeAsync((IPerformerRepository repo, CancellationToken ct) => repo.AddPerformerAsync(performer, ct));
        return performer;
    }

    public async Task<Event> EventAsync(Venue? venue = null, Performer? performer = null, DateTime? startDate = null)
    {
        venue ??= await VenueAsync();
        performer ??= await PerformerAsync();

        var @event = new Event(startDate ?? SoonEnough, venue, [performer]);
        await InScopeAsync((IEventRepository repo, CancellationToken ct) => repo.AddEventAsync(@event, ct));
        return @event;
    }

    private async Task InScopeAsync<TRepo>(Func<TRepo, CancellationToken, Task> add) where TRepo : notnull
    {
        await using var scope = _services.CreateAsyncScope();
        await add(scope.ServiceProvider.GetRequiredService<TRepo>(), CancellationToken.None);
    }
}
