namespace PaymentSystem.Query;

public class PaymentEventReadModel
{
    public Guid CheckoutId { get; init; }
    public long BookingId { get; init; }
    public Guid BuyerId { get; init; }
    public Guid? PaymentMethodId { get; init; }
    public decimal TotalAmount { get; init; }
    public string Currency { get; init; } = null!;
    public bool IsPaymentDone { get; init; }
    public DateTime CreatedAt { get; init; }
    public List<PaymentOrderReadModel> PaymentOrders { get; init; } = [];
}
