using Bookings.Application.Commands.Payments;
using Bookings.Application.Exceptions;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers.Bookings;

internal sealed class CompleteRefundCommandHandler : IRequestHandler<CompleteRefundCommand>
{
    private readonly IBookingRepository _bookings;

    public CompleteRefundCommandHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    public async Task Handle(CompleteRefundCommand request, CancellationToken cancellationToken)
    {
        var booking = await _bookings.GetByIdAsync(request.BookingId, cancellationToken);

        if (booking is null)
            throw new NotFoundException("Booking", request.BookingId.ToString());

        booking.MarkRefunded(request.RefundId);
        await _bookings.SaveChangesAsync(cancellationToken);
    }
}
