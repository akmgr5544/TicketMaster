namespace PaymentProvider.Models;

// From inclusive, To exclusive, both UTC.
public sealed record SettlementRequest(DateTime From, DateTime To);
