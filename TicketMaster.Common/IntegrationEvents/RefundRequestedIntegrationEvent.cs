namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// A paid booking was voided — a relocation took one of its seats — and the money must go back. Published by
/// Bookings, consumed by PaymentSystem, which refunds every paid order of the booking's checkout in full.
/// </summary>
public record RefundRequestedIntegrationEvent(long BookingId);
