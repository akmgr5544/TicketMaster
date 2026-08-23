using Bookings.Application.Exceptions;
using Bookings.Application.Queries;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers.Bookings;

internal sealed class CancelBookingCommandHandler : IRequestHandler<CancelBookingCommand>
{
    private readonly IBookingRepository _bookings;

    public CancelBookingCommandHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    public async Task Handle(CancelBookingCommand request, CancellationToken cancellationToken)
    {
        var booking = await _bookings.GetByIdAsync(request.BookingId, cancellationToken);

        if (booking is null || booking.UserId != request.UserId)
            throw new NotFoundException("Booking", request.BookingId.ToString());

        booking.Cancel();
        await _bookings.SaveChangesAsync(cancellationToken);
    }
}
