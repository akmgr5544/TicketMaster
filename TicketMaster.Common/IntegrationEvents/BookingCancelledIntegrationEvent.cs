namespace TicketMaster.Common.IntegrationEvents;

// A booking was cancelled in Bookings for any reason. Published by Bookings, consumed by PaymentSystem, which
// fails the booking's unsettled payment orders so a buyer cannot go on to pay for a booking that no longer exists.
public record BookingCancelledIntegrationEvent(long BookingId);
