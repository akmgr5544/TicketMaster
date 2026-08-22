namespace Bookings.Api.Requests;

/// <summary>
/// The request bodies for the checkout endpoints. They carry no user id on purpose: identity comes
/// from the gateway's header, so a caller cannot reserve or book as somebody else by putting a
/// different id in the body.
/// </summary>
public record ReserveTicketsRequest(string EventId, long[] Tickets);

public record MakeBookingRequest(string EventId, long[] Tickets);
