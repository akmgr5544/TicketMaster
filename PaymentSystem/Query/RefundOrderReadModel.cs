namespace PaymentSystem.Query;

public class RefundOrderReadModel
{
    public Guid RefundOrderId { get; init; }
    public Guid PaymentOrderId { get; init; }
    public decimal RefundAmount { get; init; }
    public string Currency { get; init; } = null!;
    public string Status { get; init; } = null!;
    public string RefundReason { get; init; } = null!;
    public DateTime CreatedAt { get; init; }
}
