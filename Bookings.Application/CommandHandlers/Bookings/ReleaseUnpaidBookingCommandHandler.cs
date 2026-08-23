using Bookings.Application.Commands;
using Bookings.Application.Exceptions;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers.Bookings;

internal sealed class ReleaseUnpaidBookingCommandHandler : IRequestHandler<ReleaseUnpaidBookingCommand>
{
    private readonly IBookingRepository _bookings;

    public ReleaseUnpaidBookingCommandHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    public async Task Handle(ReleaseUnpaidBookingCommand request, CancellationToken cancellationToken)
    {
        var booking = await _bookings.GetByIdAsync(request.BookingId, cancellationToken);

        if (booking is null)
            throw new NotFoundException("Booking", request.BookingId.ToString());

        booking.Cancel();
        await _bookings.SaveChangesAsync(cancellationToken);
    }
}
