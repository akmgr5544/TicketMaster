using Bookings.Application.Exceptions;
using Bookings.Domain.DomainEvents;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.DomainEventHandlers;

// Puts back the seats of a booking whose refund failed, and saves for BookingCancelledDomainEventHandler's reason.
// A seat a relocation cancelled stays cancelled: Ticket.Release skips anything not Booked.
internal sealed class BookingRefundFailedDomainEventHandler : INotificationHandler<BookingRefundFailedDomainEvent>
{
    private readonly ITicketsRepository _ticketsRepository;

    public BookingRefundFailedDomainEventHandler(ITicketsRepository ticketsRepository)
    {
        _ticketsRepository = ticketsRepository;
    }

    public async Task Handle(BookingRefundFailedDomainEvent notification, CancellationToken cancellationToken)
    {
        var tickets = await _ticketsRepository.GetTicketsByIdAsync([..notification.TicketIds], cancellationToken);

        if (tickets.Length != notification.TicketIds.Length)
            throw new BookingsApplicationException("Some of the booking's tickets no longer exist");

        foreach (var ticket in tickets)
        {
            ticket.Release();
        }

        await _ticketsRepository.SaveChangesAsync(cancellationToken);
    }
}
