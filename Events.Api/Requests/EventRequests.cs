using Events.Application.Commands;

namespace Events.Api.Requests;

// What a change endpoint reads from the body. The event's id comes from the route and the caller from the gateway's
// headers, so neither is here: binding the command itself made [ApiController] demand an id in the body.

public record RescheduleEventRequest(DateTime StartDate);

public record RelocateEventRequest(string VenueId);

public record ChangeEventLineupRequest(List<string> PerformerIds);

public record RepriceEventRequest(decimal TicketPrice, string Currency, List<PriceTierRequest>? PriceTiers = null);
