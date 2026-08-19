namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// The event has moved to <paramref name="VenueId"/> and its seats are now exactly
/// <paramref name="Seats"/> — the full resulting set, not the seats added or removed. A consumer
/// reconciles whatever it holds against that set, which is what makes redelivery harmless.
/// <para>
/// <paramref name="Version"/> is the producing aggregate's version at the moment of the change.
/// Delivery is at-least-once and unordered, so a consumer must ignore a message whose version is
/// not newer than what it has already applied — otherwise a redelivered older relocation silently
/// reverts a newer one.
/// </para>
/// </summary>
/// <remarks>
/// <paramref name="StartDate"/> is unchanged by a relocation, but it is carried anyway: reconciling
/// means creating tickets for seats the new venue has and the old one did not, and a ticket cannot be
/// created without it. Leaving it out would force the consumer to call back into Events for a fact
/// the producer already had.
/// </remarks>
public record EventRelocatedIntegrationEvent(
    string EventId,
    long Version,
    string VenueId,
    DateTime StartDate,
    string[] Seats);
