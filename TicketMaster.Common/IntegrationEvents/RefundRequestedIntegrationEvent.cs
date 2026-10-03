namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// Money a paid booking took must go back. Published by Bookings, consumed by PaymentSystem.
/// <para>
/// With no <c>Amount</c> it is the whole booking — a relocation voided it — and PaymentSystem refunds everything still
/// paid. With one, the customer cancelled some seats and that much, in that currency, goes back. <c>RefundId</c> names
/// the refund, so a redelivery is recognised and <see cref="BookingRefundedIntegrationEvent"/> can say which refund
/// landed; a message sent before refunds had ids carries none.
/// </para>
/// </summary>
public record RefundRequestedIntegrationEvent(
    long BookingId,
    Guid? RefundId = null,
    decimal? Amount = null,
    string? Currency = null);
