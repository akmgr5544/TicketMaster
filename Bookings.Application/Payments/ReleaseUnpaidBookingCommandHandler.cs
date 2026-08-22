using Bookings.Application.Exceptions;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.Payments;

internal sealed class ReleaseUnpaidBookingCommandHandler : IRequestHandler<ReleaseUnpaidBookingCommand>
{
    private readonly IBookingRepository _bookings;

    public ReleaseUnpaidBookingCommandHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    /// <summary>
    /// Cancelling the booking raises <c>BookingCancelledDomainEvent</c>, and the handler for that is
    /// what puts the seats back — the tickets are a separate aggregate, so this transaction changes
    /// only the booking and reaches the tickets through the event.
    /// </summary>
    public async Task Handle(ReleaseUnpaidBookingCommand request, CancellationToken cancellationToken)
    {
        var booking = await _bookings.GetByIdAsync(request.BookingId, cancellationToken);

        if (booking is null)
            throw new NotFoundException("Booking", request.BookingId.ToString());

        booking.Cancel();
        await _bookings.SaveChangesAsync(cancellationToken);
    }
}
