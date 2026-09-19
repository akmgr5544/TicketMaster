using Events.Domain.Exceptions;
using Events.Domain.Repositories;
using EventsIntegration.Fixtures;
using Microsoft.Extensions.DependencyInjection;

namespace EventsIntegration.Concurrency;

// The _etag / 412 path. Reads record the document's ETag and writes send it back as IfMatch; a
// mismatch is a real Cosmos 412 that ContainerExtensions turns into ConcurrencyConflictException.
// EventsCosmos never talks to Cosmos, so before this the conditional write was confirmed by hand
// once and nothing re-checked it. The act scope is one writer (its repository keeps its own
// ETagCache); a fresh scope is the competitor.
public sealed class ConditionalWriteTests : EventsIntegrationTest
{
    public ConditionalWriteTests(EventsFixture fixture) : base(fixture) { }

    [Fact]
    public async Task An_update_against_a_since_changed_document_is_a_concurrency_conflict()
    {
        var seeded = await Seed.EventAsync();

        // Act scope reads and caches the ETag it saw.
        var mine = await Events.GetEventByIdAsync(seeded.Id, CancellationToken.None);

        // A competing writer moves the document on, advancing its stored ETag.
        await InScopeAsync(async sp =>
        {
            var theirs = sp.GetRequiredService<IEventRepository>();
            var their = await theirs.GetEventByIdAsync(seeded.Id, CancellationToken.None);
            their!.Reschedule(DateTime.UtcNow.AddDays(30));
            await theirs.UpdateEventAsync(their, CancellationToken.None);
            return 0;
        });

        mine!.Cancel();

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => Events.UpdateEventAsync(mine, CancellationToken.None));
    }

    [Fact]
    public async Task A_delete_against_a_since_changed_document_is_a_concurrency_conflict()
    {
        var venue = await Seed.VenueAsync();

        // Act scope reads and caches the ETag the delete will send.
        await Venues.GetVenueByIdAsync(venue.Id, CancellationToken.None);

        await InScopeAsync(async sp =>
        {
            var theirs = sp.GetRequiredService<IVenueRepository>();
            var their = await theirs.GetVenueByIdAsync(venue.Id, CancellationToken.None);
            their!.Rename("Renamed");
            await theirs.UpdateVenueAsync(their, CancellationToken.None);
            return 0;
        });

        await Assert.ThrowsAsync<ConcurrencyConflictException>(
            () => Venues.DeleteVenueAsync(venue.Id, CancellationToken.None));
    }

    [Fact]
    public async Task A_second_write_in_one_scope_guards_against_the_new_etag()
    {
        var seeded = await Seed.EventAsync();
        var newVenue = await Seed.VenueAsync("Second Home");

        var mine = await Events.GetEventByIdAsync(seeded.Id, CancellationToken.None);

        mine!.Reschedule(DateTime.UtcNow.AddDays(30));
        await Events.UpdateEventAsync(mine, CancellationToken.None);

        // The stored ETag has moved on. This second write must compare against the one the first
        // write returned, not the one the original read saw — otherwise it would 412 against itself.
        mine.Relocate(newVenue);
        await Events.UpdateEventAsync(mine, CancellationToken.None);

        var reread = await InScopeAsync(sp =>
            sp.GetRequiredService<IEventRepository>().GetEventByIdAsync(seeded.Id, CancellationToken.None));

        // created = 1, reschedule = 2, relocate = 3.
        Assert.Equal(3, reread!.Version);
        Assert.Equal(newVenue.Id, reread.Venue.Id);
    }
}
