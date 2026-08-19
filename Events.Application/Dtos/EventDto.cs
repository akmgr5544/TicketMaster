using Events.Domain.Entities;

namespace Events.Application.Dtos;

/// <summary>
/// The venue and performers are the event's embedded snapshots — what they were when the event was
/// created or last changed, not what the venues and performers containers hold now.
/// <para>
/// <see cref="Status"/> is a string rather than the domain enum so the HTTP contract does not move
/// when the enum does, and so the response reads as a name instead of an ordinal.
/// </para>
/// </summary>
public record EventDto(string Id,
    DateTime StartDate,
    string Status,
    long Version,
    VenueDto Venue,
    IReadOnlyList<PerformerDto> Performers)
{
    public static EventDto From(Event @event) => new(@event.Id,
        @event.StartDate,
        @event.Status.ToString(),
        @event.Version,
        VenueDto.From(@event.Venue),
        [..@event.Performers.Select(PerformerDto.From)]);
}
