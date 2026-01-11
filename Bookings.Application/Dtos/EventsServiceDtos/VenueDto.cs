namespace Bookings.Application.Dtos.EventsServiceDtos;

public record VenueDto(string Id,
    string Name,
    string[] Seats);