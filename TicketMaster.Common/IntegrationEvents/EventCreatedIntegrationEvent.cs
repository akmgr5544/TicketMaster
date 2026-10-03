namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// <paramref name="Version"/> is defaulted rather than required so this stays an additive change:
/// producer and consumer deploy separately, and a message written before the field existed must
/// still deserialize. See the note on <see cref="EventRelocatedIntegrationEvent"/> for what
/// consumers do with it. <paramref name="Pricing"/> is defaulted for the same reason, and is null on a
/// message from before pricing existed.
/// </summary>
public record EventCreatedIntegrationEvent(
    string EventId,
    string VenueId,
    DateTime EventDate,
    string[] Seats,
    long Version = 0,
    EventPricing? Pricing = null);