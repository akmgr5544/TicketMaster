using Bookings.Application.Exceptions;
using Bookings.Application.Queries;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Domain.Exceptions;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers.Bookings;

internal sealed class CancelBookingCommandHandler : IRequestHandler<CancelBookingCommand>
{
    private readonly IBookingRepository _bookings;
    private readonly ITicketsRepository _tickets;

    public CancelBookingCommandHandler(IBookingRepository bookings, ITicketsRepository tickets)
    {
        _bookings = bookings;
        _tickets = tickets;
    }

    public async Task Handle(CancelBookingCommand request, CancellationToken cancellationToken)
    {
        var booking = await _bookings.GetByIdAsync(request.BookingId, cancellationToken);

        if (booking is null || booking.UserId != request.UserId)
            throw new NotFoundException("Booking", request.BookingId.ToString());

        if (booking.Status == BookingStatus.Payed)
            await RefundWholeAsync(booking, request.TicketIds, cancellationToken);
        else
            CancelWhole(booking, request.TicketIds);

        await _bookings.SaveChangesAsync(cancellationToken);
    }

    // An unpaid booking's checkout is already open for its whole amount and takes no change, so it is cancelled
    // whole or not at all.
    private static void CancelWhole(Booking booking, long[]? ticketIds)
    {
        if (ticketIds is not null && !NamesEverySeat(booking, ticketIds))
            throw new BookingsDomainException("An unpaid booking is cancelled whole, not seat by seat.");

        booking.Cancel();
    }

    // Refunds are whole, so a paid booking is cancelled whole too: giving some seats back would need a partial refund.
    // A well-formed request the booking's state refuses, hence 409 rather than 400.
    private async Task RefundWholeAsync(Booking booking, long[]? ticketIds, CancellationToken cancellationToken)
    {
        if (ticketIds is not null && !NamesEverySeat(booking, ticketIds))
            throw new BookingsApplicationException("A paid booking is cancelled whole, not seat by seat.");

        var tickets = await _tickets.GetTicketsByIdAsync(
            [..booking.BookedTickets.Select(bookedTicket => bookedTicket.TicketId)], cancellationToken);
        if (tickets.Any(ticket => !ticket.IsRefundableAt(DateTime.UtcNow)))
            throw new BookingsDomainException("The event has started; its seats can no longer be cancelled.");

        booking.RequestRefund();
    }

    private static bool NamesEverySeat(Booking booking, long[] ticketIds) =>
        booking.BookedTickets.Select(bookedTicket => bookedTicket.TicketId).ToHashSet().SetEquals(ticketIds);
}
