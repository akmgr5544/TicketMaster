namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// Every payment for the booking has been refunded. Published by PaymentSystem once, when the last paid order
/// is refunded; consumed by Bookings.
/// </summary>
public record BookingRefundedIntegrationEvent(long BookingId);
