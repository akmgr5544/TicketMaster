using Bookings.Application.Dtos;
using Bookings.Application.Exceptions;
using Bookings.Application.Queries;
using Bookings.Domain.Entities;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.QueryHandlers.CustomerBookings;

internal sealed class GetBookingQueryHandler : IRequestHandler<GetBookingQuery, BookingDto>
{
    private readonly IBookingRepository _bookings;

    public GetBookingQueryHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    /// <summary>
    /// Somebody else's booking answers exactly as a nonexistent one does. Distinguishing them would
    /// confirm that a given booking id exists, which is not the caller's business.
    /// </summary>
    public async Task<BookingDto> Handle(GetBookingQuery request, CancellationToken cancellationToken)
    {
        var booking = await _bookings.FindForUserAsync(request.BookingId, request.UserId, cancellationToken);

        if (booking is null)
            throw new NotFoundException("Booking", request.BookingId.ToString());

        return ToDto(booking);
    }

    internal static BookingDto ToDto(Booking booking) =>
        new(booking.Id,
            booking.Status.ToString(),
            booking.CreatedAt,
            booking.BookedTickets.Select(bookedTicket => bookedTicket.TicketId).ToArray(),
            booking.BookingHistories
                .Select(history => new BookingHistoryDto(history.BookingStatus.ToString(), history.TicketsCount))
                .ToArray());
}
