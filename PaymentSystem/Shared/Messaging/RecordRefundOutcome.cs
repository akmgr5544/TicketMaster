using Wolverine.Attributes;

namespace PaymentSystem.Shared.Messaging;

// What the PSP answered about a refund; recorded in its own transaction, apart from the PSP call. Accepted covers the
// PSP's Pending too: it has taken the instruction.
[MessageIdentity("record-refund-outcome")]
public sealed record RecordRefundOutcome(Guid RefundOrderId, bool Accepted, string? ProviderReference, string? FailureReason);
