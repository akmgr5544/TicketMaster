namespace PaymentProvider.Models;

public enum RefundStatus
{
    // Accepted by the provider but not yet final; a card refund can still fail days later.
    Pending,
    Succeeded,
    Failed,
}
