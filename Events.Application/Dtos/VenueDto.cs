using Events.Domain.Entities;

namespace Events.Application.Dtos;

public record VenueDto(string Id,
    string Name,
    string Address,
    double Latitude,
    double Longitude,
    IReadOnlyList<string> Seats)
{
    public static VenueDto From(Venue venue) => new(venue.Id,
        venue.Name,
        venue.Address,
        venue.Location.Latitude,
        venue.Location.Longitude,
        venue.Seats);
}
