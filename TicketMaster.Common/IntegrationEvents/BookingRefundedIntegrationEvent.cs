namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// A refund Bookings asked for has landed. Published by PaymentSystem, consumed by Bookings. For a partial refund it
/// goes out as soon as that refund is recorded; for a whole-booking refund, once every paid order is refunded.
/// <c>RefundId</c> is the one <see cref="RefundRequestedIntegrationEvent"/> carried, and null for a refund nobody
/// named — a booking cancelled after its payment had already been taken.
/// </summary>
public record BookingRefundedIntegrationEvent(long BookingId, Guid? RefundId = null);
