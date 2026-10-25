using Wolverine.Attributes;

namespace PaymentSystem.Shared.Messaging;

// Scheduled when a checkout opens; stored until due, so it survives a restart. The identity is pinned so a rename
// cannot strand the envelopes already stored under the old name.
[MessageIdentity("checkout-expiry-due")]
public sealed record CheckoutExpiryDue(long BookingId);
