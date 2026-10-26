namespace PaymentProvider.Models;

public enum SettlementLineKind
{
    Charge,
    Refund,
    // A refund the provider had reported done came back: the money never reached the buyer.
    RefundFailure,
}
