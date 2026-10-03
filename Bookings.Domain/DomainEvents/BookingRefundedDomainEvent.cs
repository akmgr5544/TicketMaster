using Bookings.Domain.Abstractions;

namespace Bookings.Domain.DomainEvents;

/// <summary>
/// The money is back with the buyer, so the seats the booking still holds go back on sale. Carries ticket ids
/// for the reason <see cref="BookingCancelledDomainEvent"/> does.
/// </summary>
public record BookingRefundedDomainEvent(long BookingId, long[] TicketIds) : DomainEvent;
