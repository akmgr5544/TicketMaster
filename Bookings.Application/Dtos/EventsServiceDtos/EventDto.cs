using TicketMaster.Common.IntegrationEvents;

namespace Bookings.Application.Dtos.EventsServiceDtos;

public record EventDto(string Id, VenueDto Venue, EventPricing? Pricing = null);
