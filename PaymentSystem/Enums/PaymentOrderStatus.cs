namespace PaymentSystem.Enums;

public enum PaymentOrderStatus
{
    NotStarted,
    Executing,
    Success,
    Failed,
    // The provider accepted the refund. A card refund can still fail days later; nothing hears about that yet.
    Refunded
}
