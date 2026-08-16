namespace Events.Application.Dtos;

public record VenueDto(string Id,
    string Name,
    string Address,
    double Latitude,
    double Longitude);
