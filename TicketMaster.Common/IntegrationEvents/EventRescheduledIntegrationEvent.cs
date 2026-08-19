namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// The event now starts at <paramref name="StartDate"/>. Carries the resulting date rather than a
/// shift, so applying it twice lands in the same place.
/// </summary>
public record EventRescheduledIntegrationEvent(
    string EventId,
    long Version,
    DateTime StartDate);
