using Bookings.Application.Dtos;
using Bookings.Application.Queries;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.QueryHandlers.CustomerBookings;

internal sealed class ListBookingsQueryHandler : IRequestHandler<ListBookingsQuery, BookingDto[]>
{
    private readonly IBookingRepository _bookings;

    public ListBookingsQueryHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    public async Task<BookingDto[]> Handle(ListBookingsQuery request, CancellationToken cancellationToken)
    {
        var bookings = await _bookings.ListForUserAsync(request.UserId,
            (request.Page - 1) * request.PageSize,
            request.PageSize,
            cancellationToken);

        return bookings.Select(GetBookingQueryHandler.ToDto).ToArray();
    }
}
