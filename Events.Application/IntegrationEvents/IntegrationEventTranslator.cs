using Events.Domain.Abstractions;
using Events.Domain.DomainEvents;
using Events.Domain.ValueObjects;
using TicketMaster.Common.IntegrationEvents;

namespace Events.Application.IntegrationEvents;

/// <summary>
/// Turns this service's private domain events into the public contracts in
/// <c>TicketMaster.Common</c>.
/// <para>
/// The translation lives here, in Application, because Events.Domain has no references at all and
/// must not learn about the shared contracts. It is also the boundary where a domain event is
/// allowed to have no public counterpart.
/// </para>
/// </summary>
public static class IntegrationEventTranslator
{
    public static IEnumerable<object> Translate(IEnumerable<IDomainEvent> domainEvents) =>
        domainEvents.Select(Translate).OfType<object>();

    /// <summary>
    /// Returns null when a domain event is deliberately internal. A lineup change is the case
    /// today: nothing outside this service depends on who is performing, so publishing it would be a
    /// contract with no consumer.
    /// </summary>
    private static object? Translate(IDomainEvent domainEvent) => domainEvent switch
    {
        EventCreatedDomainEvent e =>
            new EventCreatedIntegrationEvent(e.EventId, e.VenueId, e.StartDate, [..e.Seats], e.Version,
                PricingOf(e.TicketPrice, e.OrganizerId)),

        EventRescheduledDomainEvent e =>
            new EventRescheduledIntegrationEvent(e.EventId, e.Version, e.StartDate),

        EventRelocatedDomainEvent e =>
            new EventRelocatedIntegrationEvent(e.EventId, e.Version, e.VenueId, e.StartDate, [..e.Seats],
                PricingOf(e.TicketPrice, e.OrganizerId)),

        EventCancelledDomainEvent e =>
            new EventCancelledIntegrationEvent(e.EventId, e.Version),

        _ => null
    };

    // Null for an event stored before pricing existed; Bookings then creates its tickets unpriced and unsellable.
    private static EventPricing? PricingOf(TicketPrice? price, Guid organizerId) =>
        price is null ? null : new EventPricing(price.Amount, price.Currency, organizerId);
}
