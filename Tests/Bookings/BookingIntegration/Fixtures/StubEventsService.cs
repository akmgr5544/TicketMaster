using Bookings.Application.Dtos.EventsServiceDtos;
using Bookings.Application.Services.Interfaces;

namespace BookingIntegration.Fixtures;

/// <summary>
/// Stands in for the catalogue. Stubbing an outbound call to another process is the one exception to
/// this suite's no-fakes rule: the real implementation is a gRPC client, and running it would test
/// Events, not Bookings. See the `rpc` skill.
/// </summary>
public sealed class StubEventsService : IEventsService
{
    private readonly Dictionary<string, EventDto> _catalogue = [];

    /// <summary>Set to make every call throw, standing in for Events being unreachable.</summary>
    public Exception? Fails { get; set; }

    public void Knows(string eventId, string venueId, params string[] seats) =>
        _catalogue[eventId] = new EventDto(eventId, new VenueDto(venueId, $"{venueId} name", seats));

    public Task<EventDto?> GetEventByIdAsync(string id, CancellationToken cancellationToken)
    {
        if (Fails is not null)
            throw Fails;

        return Task.FromResult(_catalogue.GetValueOrDefault(id));
    }
}
