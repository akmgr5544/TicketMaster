using Bookings.Domain.Abstractions;

namespace Bookings.Domain.DomainEvents;

// No amount: everything still paid, which is what a relocation voiding the booking asks for.
public record BookingRefundRequestedDomainEvent(long BookingId, Guid RefundId, decimal? Amount, string? Currency)
    : DomainEvent;
