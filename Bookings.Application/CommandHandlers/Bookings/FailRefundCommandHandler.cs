using Bookings.Application.Commands.Payments;
using Bookings.Application.Exceptions;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers.Bookings;

internal sealed class FailRefundCommandHandler : IRequestHandler<FailRefundCommand>
{
    private readonly IBookingRepository _bookings;

    public FailRefundCommandHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    public async Task Handle(FailRefundCommand request, CancellationToken cancellationToken)
    {
        var booking = await _bookings.GetByIdAsync(request.BookingId, cancellationToken);

        if (booking is null)
            throw new NotFoundException("Booking", request.BookingId.ToString());

        booking.MarkRefundFailed(request.RefundId);
        await _bookings.SaveChangesAsync(cancellationToken);
    }
}
