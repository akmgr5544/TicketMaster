using Bookings.Domain.Abstractions;

namespace Bookings.Domain.DomainEvents;

/// <summary>
/// The booking has been called off and the seats it held should go back on sale. Carries ticket ids
/// for the same reason <see cref="BookingCreatedDomainEvent"/> does: tickets are their own aggregate,
/// and a handler reaches them through their own root rather than through the booking's.
/// </summary>
public record BookingCancelledDomainEvent(long[] TicketIds) : DomainEvent;
