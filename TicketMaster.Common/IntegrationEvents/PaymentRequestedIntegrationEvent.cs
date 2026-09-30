namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// A booking has been placed and is awaiting payment. Published by Bookings, consumed by PaymentSystem.
/// Carries the money because Bookings owns no pricing today — see the design doc's amount-source note.
/// </summary>
public record PaymentRequestedIntegrationEvent(
    long BookingId,
    Guid BuyerId,
    Guid SellerId,
    decimal Amount,
    string Currency);
