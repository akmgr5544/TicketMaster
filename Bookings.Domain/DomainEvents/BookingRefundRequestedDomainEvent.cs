using Bookings.Domain.Abstractions;

namespace Bookings.Domain.DomainEvents;

public record BookingRefundRequestedDomainEvent(long BookingId) : DomainEvent;
