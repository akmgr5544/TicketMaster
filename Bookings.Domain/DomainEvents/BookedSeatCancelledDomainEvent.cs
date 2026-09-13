using Bookings.Domain.Abstractions;

namespace Bookings.Domain.DomainEvents;

public record BookedSeatCancelledDomainEvent(long TicketId) : DomainEvent;
