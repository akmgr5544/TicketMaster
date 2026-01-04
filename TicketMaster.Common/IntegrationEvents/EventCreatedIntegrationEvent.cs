namespace TicketMaster.Common.IntegrationEvents;

public record EventCreatedIntegrationEvent(
    string EventId,
    string VenueId,
    DateTime EventDate,
    string[] Seats);