namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// What every ticket for an event costs and who is paid for it. One object rather than three optional
/// fields, because the three only make sense together.
/// </summary>
public record EventPricing(decimal TicketPrice, string Currency, Guid OrganizerId);
