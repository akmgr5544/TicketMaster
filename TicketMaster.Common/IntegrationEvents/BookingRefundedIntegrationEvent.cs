namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// A refund Bookings asked for has landed: the whole of the booking's payment is back with the buyer. Published by
/// PaymentSystem once the PSP reports the refund final, consumed by Bookings and Notifications. <c>RefundId</c> is
/// the one <see cref="RefundRequestedIntegrationEvent"/> carried, and null for a refund nobody named — a booking
/// cancelled after its payment had already been taken.
/// </summary>
public record BookingRefundedIntegrationEvent(long BookingId, Guid? RefundId = null);
