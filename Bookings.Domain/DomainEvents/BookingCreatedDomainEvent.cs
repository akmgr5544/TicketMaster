using Bookings.Domain.Abstractions;

namespace Bookings.Domain.DomainEvents;

/// <summary>
/// Carries ticket ids rather than <c>Ticket</c> instances. Tickets are their own aggregate, so a
/// handler that received the instances could change them without going through their root — which is
/// how the booked status came to be set on an entity nobody was saving.
/// <para>
/// There is deliberately no booking id: the event is raised inside <c>Booking.Create</c>, before the
/// database has assigned one, so any id captured here would be zero.
/// </para>
/// </summary>
public record BookingCreatedDomainEvent(long[] TicketIds) : DomainEvent;
