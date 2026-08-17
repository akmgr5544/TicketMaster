using Events.Application.CommandHandlers;
using Events.Application.Commands;
using Events.Application.Exceptions;
using Events.Application.Queries;
using Events.Application.QueryHandlers;
using Events.Domain.Entities;
using Events.Domain.Exceptions;
using Events.Domain.ValueObjects;
using EventsApplication.Fakes;

namespace EventsApplication;

public class VenueHandlerTests
{
    private static readonly GeoLocation Yerevan = new(40.1872, 44.5152);
    private static readonly GeoLocation Tbilisi = new(41.7151, 44.8271);

    private readonly FakeVenueRepository _venues = new();
    private readonly FakeEventRepository _events = new();

    private static Venue AVenue() =>
        new("Karen Demirchyan Complex", "Tsitsernakaberd Hwy 1", Yerevan, ["A1", "A2"]);

    // --- Get ---

    [Fact]
    public async Task Get_returns_the_venue_as_a_dto()
    {
        var venue = AVenue();
        _venues.Seed(venue);

        var result = await new GetVenueQueryHandler(_venues)
            .Handle(new GetVenueQuery(venue.Id), CancellationToken.None);

        Assert.Equal(venue.Id, result.Id);
        Assert.Equal("Karen Demirchyan Complex", result.Name);
        Assert.Equal(40.1872, result.Latitude);
        Assert.Equal(44.5152, result.Longitude);
        Assert.Equal(["A1", "A2"], result.Seats);
    }

    [Fact]
    public async Task Get_throws_when_the_venue_does_not_exist()
    {
        var handler = new GetVenueQueryHandler(_venues);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetVenueQuery("missing"), CancellationToken.None));
    }

    // --- List ---

    [Fact]
    public async Task List_passes_the_continuation_token_through_in_both_directions()
    {
        _venues.Seed(AVenue(), AVenue());
        _venues.NextContinuationToken = "next-page";

        var result = await new ListVenuesQueryHandler(_venues)
            .Handle(new ListVenuesQuery(10, "from-client"), CancellationToken.None);

        Assert.Equal("from-client", _venues.LastContinuationTokenRequested);
        Assert.Equal("next-page", result.ContinuationToken);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task List_reports_no_token_when_the_results_are_exhausted()
    {
        _venues.Seed(AVenue());
        _venues.NextContinuationToken = null;

        var result = await new ListVenuesQueryHandler(_venues)
            .Handle(new ListVenuesQuery(10, null), CancellationToken.None);

        Assert.Null(result.ContinuationToken);
    }

    // --- Update ---

    [Fact]
    public async Task Update_renames_and_relocates_the_venue()
    {
        var venue = AVenue();
        _venues.Seed(venue);

        await new UpdateVenueCommandHandler(_venues).Handle(
            new UpdateVenueCommand(venue.Id, "Demirchyan Arena", "New Address", Tbilisi.Latitude, Tbilisi.Longitude),
            CancellationToken.None);

        var updated = await _venues.GetVenueByIdAsync(venue.Id, CancellationToken.None);
        Assert.Equal("Demirchyan Arena", updated!.Name);
        Assert.Equal("New Address", updated.Address);
        Assert.Equal(Tbilisi, updated.Location);
    }

    [Fact]
    public async Task Update_leaves_the_seats_untouched()
    {
        var venue = AVenue();
        _venues.Seed(venue);

        await new UpdateVenueCommandHandler(_venues).Handle(
            new UpdateVenueCommand(venue.Id, "Renamed", "Elsewhere", Tbilisi.Latitude, Tbilisi.Longitude),
            CancellationToken.None);

        var updated = await _venues.GetVenueByIdAsync(venue.Id, CancellationToken.None);
        Assert.Equal(["A1", "A2"], updated!.Seats);
    }

    [Fact]
    public async Task Update_throws_when_the_venue_does_not_exist()
    {
        var handler = new UpdateVenueCommandHandler(_venues);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new UpdateVenueCommand("missing", "n", "a", 0, 0), CancellationToken.None));
    }

    [Fact]
    public async Task Update_rejects_a_blank_name_without_persisting_anything()
    {
        var venue = AVenue();
        _venues.Seed(venue);

        var handler = new UpdateVenueCommandHandler(_venues);

        await Assert.ThrowsAsync<EventsDomainException>(() =>
            handler.Handle(new UpdateVenueCommand(venue.Id, "  ", "a", 0, 0), CancellationToken.None));

        var unchanged = await _venues.GetVenueByIdAsync(venue.Id, CancellationToken.None);
        Assert.Equal("Karen Demirchyan Complex", unchanged!.Name);
    }

    // --- Delete ---

    [Fact]
    public async Task Delete_removes_a_venue_with_no_upcoming_events()
    {
        var venue = AVenue();
        _venues.Seed(venue);
        _events.UpcomingEventCount = 0;

        await new DeleteVenueCommandHandler(_venues, _events)
            .Handle(new DeleteVenueCommand(venue.Id), CancellationToken.None);

        Assert.False(_venues.Contains(venue.Id));
    }

    [Fact]
    public async Task Delete_is_refused_when_the_venue_has_upcoming_events()
    {
        var venue = AVenue();
        _venues.Seed(venue);
        _events.UpcomingEventCount = 3;

        var handler = new DeleteVenueCommandHandler(_venues, _events);

        await Assert.ThrowsAsync<EventsApplicationException>(() =>
            handler.Handle(new DeleteVenueCommand(venue.Id), CancellationToken.None));

        Assert.True(_venues.Contains(venue.Id));
    }

    [Fact]
    public async Task Delete_throws_when_the_venue_does_not_exist()
    {
        var handler = new DeleteVenueCommandHandler(_venues, _events);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new DeleteVenueCommand("missing"), CancellationToken.None));
    }

    // --- Add ---

    [Fact]
    public async Task Add_returns_the_id_of_the_created_venue()
    {
        var id = await new AddVenueCommandHandler(_venues).Handle(
            new AddVenueCommand("A Venue", "An Address", 40.1872, 44.5152, ["A1"]),
            CancellationToken.None);

        Assert.False(string.IsNullOrWhiteSpace(id));
        Assert.True(_venues.Contains(id));
    }
}
