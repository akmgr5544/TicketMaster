using Bookings.Application.Exceptions;
using Bookings.Domain.DomainEvents;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.DomainEventHandlers;

/// <summary>
/// Puts the seats a cancelled booking was holding back on sale.
/// <para>
/// The mirror image of <see cref="BookingCreatedDomainEventHandler"/>, and it saves for the same
/// reason: dispatch happens around a save that has already been decided, so a change made here has to
/// be persisted here. The surrounding transaction is what keeps this and the booking's own change
/// atomic — a booking must never be marked cancelled without its seats actually going back.
/// </para>
/// </summary>
public class BookingCancelledDomainEventHandler : INotificationHandler<BookingCancelledDomainEvent>
{
    private readonly ITicketsRepository _ticketsRepository;

    public BookingCancelledDomainEventHandler(ITicketsRepository ticketsRepository)
    {
        _ticketsRepository = ticketsRepository;
    }

    public async Task Handle(BookingCancelledDomainEvent notification, CancellationToken cancellationToken)
    {
        var tickets = await _ticketsRepository.GetTicketsByIdAsync([..notification.TicketIds], cancellationToken);

        if (tickets.Length != notification.TicketIds.Length)
            throw new BookingsApplicationException("Some of the cancelled booking's tickets no longer exist");

        foreach (var ticket in tickets)
        {
            ticket.Release();
        }

        await _ticketsRepository.SaveChangesAsync(cancellationToken);
    }
}
