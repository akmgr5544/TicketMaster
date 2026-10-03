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
            await RequestRefundAsync(booking, request.TicketIds, cancellationToken);
        else
            CancelWhole(booking, request.TicketIds);

        await _bookings.SaveChangesAsync(cancellationToken);
    }

    // An unpaid booking's checkout is already open for its whole amount and takes no change, so it is cancelled
    // whole or not at all.
    private static void CancelWhole(Booking booking, long[]? ticketIds)
    {
        var all = booking.BookedTickets.Select(bookedTicket => bookedTicket.TicketId).ToHashSet();
        if (ticketIds is not null && !all.SetEquals(ticketIds))
            throw new BookingsDomainException("An unpaid booking is cancelled whole, not seat by seat.");

        booking.Cancel();
    }

    // No seats named: every seat no refund covers yet. Each seat gives back what it was charged — a booked ticket
    // keeps its price through a repricing — summed the way MakeBookingCommandHandler summed the charge.
    private async Task RequestRefundAsync(Booking booking, long[]? ticketIds, CancellationToken cancellationToken)
    {
        ticketIds ??= booking.BookedTickets
            .Where(bookedTicket => bookedTicket.RefundId is null)
            .Select(bookedTicket => bookedTicket.TicketId)
            .ToArray();
        if (ticketIds.Length == 0)
            throw new BookingsDomainException("Every seat of this booking is already being refunded.");

        var tickets = await _tickets.GetTicketsByIdAsync([..ticketIds.Distinct()], cancellationToken);
        if (tickets.Length != ticketIds.Distinct().Count())
            throw new BookingsDomainException("Some of those seats do not exist.");
        if (tickets.Any(ticket => !ticket.IsRefundableAt(DateTime.UtcNow)))
            throw new BookingsDomainException("The event has started; its seats can no longer be cancelled.");

        var pricings = tickets.Select(ticket => ticket.Pricing!).ToArray();
        booking.RequestRefund(ticketIds, pricings.Sum(pricing => pricing.Price), pricings[0].Currency);
    }
}
