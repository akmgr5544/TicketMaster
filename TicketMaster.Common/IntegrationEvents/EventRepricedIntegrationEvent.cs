namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// The event's pricing is now exactly <paramref name="Pricing"/> — the whole resulting state, not what changed. A
/// consumer ignores a message whose <paramref name="Version"/> is not newer than what it has applied.
/// </summary>
public record EventRepricedIntegrationEvent(string EventId, long Version, EventPricing Pricing);
