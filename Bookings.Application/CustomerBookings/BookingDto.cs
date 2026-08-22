namespace Bookings.Application.CustomerBookings;

/// <summary>
/// A booking as its owner sees it. A projection rather than the aggregate: <c>Booking</c> carries
/// domain events and owned collections that have no business crossing an HTTP boundary.
/// <para>
/// There is no timestamp because <c>Booking</c> has none to give — see the note on
/// <c>ListBookingsQuery</c>.
/// </para>
/// </summary>
public record BookingDto(long Id, string Status, long[] TicketIds, BookingHistoryDto[] History);

public record BookingHistoryDto(string Status, int TicketsCount);
