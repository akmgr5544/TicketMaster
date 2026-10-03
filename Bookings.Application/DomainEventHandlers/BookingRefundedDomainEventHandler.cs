using Bookings.Application.Exceptions;
using Bookings.Domain.DomainEvents;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.DomainEventHandlers;

// Puts back the seats a refunded booking still holds, and saves for BookingCancelledDomainEventHandler's reason.
// The seat whose cancellation caused the refund stays cancelled: Ticket.Release skips anything not Booked.
internal sealed class BookingRefundedDomainEventHandler : INotificationHandler<BookingRefundedDomainEvent>
{
    private readonly ITicketsRepository _ticketsRepository;

    public BookingRefundedDomainEventHandler(ITicketsRepository ticketsRepository)
    {
        _ticketsRepository = ticketsRepository;
    }

    public async Task Handle(BookingRefundedDomainEvent notification, CancellationToken cancellationToken)
    {
        var tickets = await _ticketsRepository.GetTicketsByIdAsync([..notification.TicketIds], cancellationToken);

        if (tickets.Length != notification.TicketIds.Length)
            throw new BookingsApplicationException("Some of the refunded booking's tickets no longer exist");

        foreach (var ticket in tickets)
        {
            ticket.Release();
        }

        await _ticketsRepository.SaveChangesAsync(cancellationToken);
    }
}
