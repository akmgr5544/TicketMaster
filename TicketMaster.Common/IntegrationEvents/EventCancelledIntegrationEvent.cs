namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// The event has been called off. The catalogue keeps the event document, so consumers should
/// cancel what they hold rather than delete it.
/// </summary>
public record EventCancelledIntegrationEvent(
    string EventId,
    long Version);
