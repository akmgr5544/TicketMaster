namespace PaymentProvider.Models;

// One movement of money the provider settled. ProviderReference is what the provider's other answers name the same
// thing by — a charge by its payment's reference, a refund by the refund's — so it matches what was stored from them.
// PaymentOrderId and RefundId are what the provider was tagged with, when it carries them.
public sealed record SettlementLine(
    string ProviderReference,
    SettlementLineKind Kind,
    decimal Amount,
    string Currency,
    Guid? PaymentOrderId = null,
    Guid? RefundId = null);
