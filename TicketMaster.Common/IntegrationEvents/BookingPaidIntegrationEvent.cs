namespace TicketMaster.Common.IntegrationEvents;

/// <summary>
/// The payment for <paramref name="BookingId"/> has succeeded. Published by whatever takes the money;
/// Bookings only consumes it.
/// <para>
/// Deliberately carries no version. There is one payment outcome per booking, and the two possible
/// outcomes are made safe against unordered delivery by the booking itself refusing to move from a
/// state it has already settled into — so whichever outcome lands first is the one that sticks.
/// </para>
/// </summary>
public record BookingPaidIntegrationEvent(long BookingId);
