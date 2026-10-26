using Wolverine.Attributes;

namespace PaymentSystem.Shared.Messaging;

// What the PSP answered about a refund; recorded in its own transaction, apart from the PSP call. Accepted without
// Final is the PSP's Pending: it has taken the instruction but not finished. Final comes last and defaults to true so
// envelopes stored before it existed still read as the final answers they were.
[MessageIdentity("record-refund-outcome")]
public sealed record RecordRefundOutcome(
    Guid RefundOrderId, bool Accepted, string? ProviderReference, string? FailureReason, bool Final = true);
