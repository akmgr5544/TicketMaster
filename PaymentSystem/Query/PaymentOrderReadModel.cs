namespace PaymentSystem.Query;

public class PaymentOrderReadModel
{
    public Guid PaymentOrderId { get; init; }
    public Guid CheckoutId { get; init; }
    public Guid SellerId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = null!;
    public string Status { get; init; } = null!;
    // For calling the PSP back about the order; never returned by an endpoint.
    public string? Provider { get; init; }
    public string? PspTokenRef { get; init; }
    public DateTime? ExecutingSince { get; init; }
    public bool WalletUpdated { get; init; }
    public bool LedgerUpdated { get; init; }
    public DateTime CreatedAt { get; init; }
}
