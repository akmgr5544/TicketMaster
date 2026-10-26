namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// A paid booking's money must go back — all of it. Published by Bookings (a relocation voided the booking, or the
/// customer cancelled it), consumed by PaymentSystem, which refunds everything still paid.
/// <para>
/// Refunds are whole only: a paid booking cannot be cancelled seat by seat, so there is no amount. <c>RefundId</c>
/// names the refund, so a redelivery is recognised and <see cref="BookingRefundedIntegrationEvent"/> can say which
/// refund landed; a message sent before refunds had ids carries none.
/// </para>
/// </summary>
public record RefundRequestedIntegrationEvent(long BookingId, Guid? RefundId = null);
