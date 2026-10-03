using Bookings.Domain.Entities;

namespace Bookings.Application.Dtos.EventsServiceDtos;

public record EventDto(string Id, VenueDto Venue, TicketPricing? Pricing = null);
