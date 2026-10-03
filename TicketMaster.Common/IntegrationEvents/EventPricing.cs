namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// What every ticket for an event costs and who is paid for it. One object rather than several optional
/// fields, because they only make sense together. <paramref name="SeatPrices"/> holds only the seats whose price
/// differs from <paramref name="TicketPrice"/> (the event's price tiers, flattened); it is defaulted so a message
/// from before tiers existed still deserializes.
/// </summary>
public record EventPricing(
    decimal TicketPrice,
    string Currency,
    Guid OrganizerId,
    IReadOnlyDictionary<string, decimal>? SeatPrices = null)
{
    public decimal PriceFor(string seat) =>
        SeatPrices is not null && SeatPrices.TryGetValue(seat, out var price) ? price : TicketPrice;
}
