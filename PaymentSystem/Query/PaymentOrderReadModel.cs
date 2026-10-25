namespace PaymentSystem.Query;

public class PaymentOrderReadModel
{
    public Guid PaymentOrderId { get; init; }
    public Guid CheckoutId { get; init; }
    public Guid SellerId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = null!;
    public string Status { get; init; } = null!;
    public bool WalletUpdated { get; init; }
    public bool LedgerUpdated { get; init; }
    public DateTime CreatedAt { get; init; }
}
