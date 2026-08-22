using Bookings.Application.Exceptions;
using Bookings.Domain.DomainEvents;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.DomainEventHandlers;

/// <summary>
/// Marks the tickets a new booking covers as booked.
/// <para>
/// It loads them by id and saves them itself, and both halves matter. Loading by id keeps tickets
/// behind their own aggregate root instead of taking instances out of the event; saving is necessary
/// because dispatch happens around a save that has already been decided, so anything changed here
/// would otherwise sit in the change tracker and be thrown away. The surrounding transaction is what
/// makes this save and the booking's own insert atomic.
/// </para>
/// </summary>
public class BookingCreatedDomainEventHandler : INotificationHandler<BookingCreatedDomainEvent>
{
    private readonly ITicketsRepository _ticketsRepository;

    public BookingCreatedDomainEventHandler(ITicketsRepository ticketsRepository)
    {
        _ticketsRepository = ticketsRepository;
    }

    public async Task Handle(BookingCreatedDomainEvent notification, CancellationToken cancellationToken)
    {
        var tickets = await _ticketsRepository.GetTicketsByIdAsync([..notification.TicketIds], cancellationToken);

        if (tickets.Length != notification.TicketIds.Length)
            throw new BookingException("Some of the booked tickets no longer exist");

        foreach (var ticket in tickets)
        {
            ticket.Book();
        }

        await _ticketsRepository.SaveChangesAsync(cancellationToken);
    }
}
