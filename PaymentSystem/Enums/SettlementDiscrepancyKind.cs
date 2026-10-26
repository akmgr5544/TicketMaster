namespace PaymentSystem.Enums;

public enum SettlementDiscrepancyKind
{
    // The PSP took money for an order we count failed, expired or cancelled.
    ChargeForUnpaidOrder,
    // The PSP took money for nothing we know of.
    UnknownCharge,
    ChargeAmountMismatch,
    // The PSP gave money back that we have no record of giving back.
    RefundNotRecorded,
    RefundAmountMismatch,
    // The PSP took back a refund we had counted done; the undo has been recorded, a person must still pay the buyer.
    RefundFailedAfterSuccess,
    // We count the order paid, but the PSP never settled it.
    OrderNotSettled,
    // We count the refund done, but the PSP never settled it.
    RefundNotSettled
}
