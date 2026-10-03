namespace Bookings.Domain.Entities;

/// <summary>
/// What the ticket sells for and who is paid, copied from the event when the ticket is created. Bookings
/// owns no pricing; this is a replica of what Events announced.
/// </summary>
public sealed record TicketPricing(decimal Price, string Currency, Guid SellerId);
