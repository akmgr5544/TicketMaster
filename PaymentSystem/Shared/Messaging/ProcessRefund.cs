using Wolverine.Attributes;

namespace PaymentSystem.Shared.Messaging;

// Sent once a refund is recorded and committed: the PSP is called outside the transaction that recorded it.
[MessageIdentity("process-refund")]
public sealed record ProcessRefund(Guid RefundOrderId);
