using Bookings.Domain.Abstractions;

namespace Bookings.Domain.DomainEvents;

/// <summary>
/// The refund failed, but the booking is still void: the seats it holds go back on sale while a person returns the
/// money. Carries ticket ids for the reason <see cref="BookingCancelledDomainEvent"/> does.
/// </summary>
public record BookingRefundFailedDomainEvent(long BookingId, long[] TicketIds) : DomainEvent;
