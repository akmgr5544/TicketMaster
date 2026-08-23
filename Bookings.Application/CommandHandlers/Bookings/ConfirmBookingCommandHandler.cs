using Bookings.Application.Commands;
using Bookings.Application.Exceptions;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers.Bookings;

internal sealed class ConfirmBookingCommandHandler : IRequestHandler<ConfirmBookingPaymentCommand>
{
    private readonly IBookingRepository _bookings;

    public ConfirmBookingCommandHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    public async Task Handle(ConfirmBookingPaymentCommand request, CancellationToken cancellationToken)
    {
        var booking = await _bookings.GetByIdAsync(request.BookingId, cancellationToken);

        if (booking is null)
            throw new NotFoundException("Booking", request.BookingId.ToString());

        booking.MarkPaid();
        await _bookings.SaveChangesAsync(cancellationToken);
    }
}
