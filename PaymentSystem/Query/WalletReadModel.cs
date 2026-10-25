namespace PaymentSystem.Query;

public class WalletReadModel
{
    public Guid WalletId { get; init; }
    public Guid UserId { get; init; }
    public decimal CurrentBalance { get; init; }
    public string Currency { get; init; } = null!;
    public DateTime UpdatedAt { get; init; }
}
