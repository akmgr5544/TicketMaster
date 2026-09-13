using Bookings.Domain.DomainEvents;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.DomainEventHandlers;

internal sealed class BookedSeatCancelledDomainEventHandler : INotificationHandler<BookedSeatCancelledDomainEvent>
{
    private readonly IBookingRepository _bookings;

    public BookedSeatCancelledDomainEventHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    public async Task Handle(BookedSeatCancelledDomainEvent notification, CancellationToken cancellationToken)
    {
        var booking = await _bookings.FindByTicketIdAsync(notification.TicketId, cancellationToken);
        if (booking is null)
            return;

        booking.OnBookedSeatCancelled();

        await _bookings.SaveChangesAsync(cancellationToken);
    }
}
