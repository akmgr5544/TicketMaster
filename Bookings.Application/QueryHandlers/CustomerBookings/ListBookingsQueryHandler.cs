using Bookings.Application.Dtos;
using Bookings.Application.Queries;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.QueryHandlers.CustomerBookings;

internal sealed class ListBookingsQueryHandler : IRequestHandler<ListBookingsQuery, PagedResult<BookingDto>>
{
    private readonly IBookingRepository _bookings;

    public ListBookingsQueryHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    public async Task<PagedResult<BookingDto>> Handle(ListBookingsQuery request, CancellationToken cancellationToken)
    {
        var total = await _bookings.CountForUserAsync(request.UserId, cancellationToken);

        var bookings = await _bookings.ListForUserAsync(request.UserId,
            (request.Page - 1) * request.PageSize,
            request.PageSize,
            cancellationToken);

        var items = bookings.Select(GetBookingQueryHandler.ToDto).ToArray();

        return new PagedResult<BookingDto>(items, request.Page, request.PageSize, total);
    }
}
