using Bookings.Domain.Entities;
using TicketMaster.Common.IntegrationEvents;

namespace Bookings.Application.IntegrationEventHandlers;

internal static class EventPricingExtensions
{
    // The event's organizer is the seller: Events names who is paid, Bookings only carries it to Payments.
    public static TicketPricing? ToTicketPricing(this EventPricing? pricing) =>
        pricing is null ? null : new TicketPricing(pricing.TicketPrice, pricing.Currency, pricing.OrganizerId);
}
