namespace Bookings.Application.Dtos.EventsServiceDtos;

public record EventDto(string Id,
    DateTime StartDate,
    VenueDto Venue);