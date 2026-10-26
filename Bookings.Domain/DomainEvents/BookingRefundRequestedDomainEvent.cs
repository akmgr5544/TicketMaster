using Bookings.Domain.Abstractions;

namespace Bookings.Domain.DomainEvents;

// No amount: refunds are whole, so it asks for everything paid.
public record BookingRefundRequestedDomainEvent(long BookingId, Guid RefundId) : DomainEvent;
