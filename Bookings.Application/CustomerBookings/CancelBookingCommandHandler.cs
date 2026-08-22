using Bookings.Application.Exceptions;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CustomerBookings;

internal sealed class CancelBookingCommandHandler : IRequestHandler<CancelBookingCommand>
{
    private readonly IBookingRepository _bookings;

    public CancelBookingCommandHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    /// <summary>
    /// Loaded tracked and then checked for ownership, rather than fetched with the owner in the query
    /// as the reads do: this one has to save, and the untracked read cannot.
    /// <para>
    /// The seats go back through <c>BookingCancelledDomainEvent</c>, so this transaction changes only
    /// the booking. <c>Booking.Cancel()</c> refuses a booking that has been paid for, which is what
    /// stops this becoming an unrefunded cancellation.
    /// </para>
    /// </summary>
    public async Task Handle(CancelBookingCommand request, CancellationToken cancellationToken)
    {
        var booking = await _bookings.GetByIdAsync(request.BookingId, cancellationToken);

        if (booking is null || booking.UserId != request.UserId)
            throw new NotFoundException("Booking", request.BookingId.ToString());

        booking.Cancel();
        await _bookings.SaveChangesAsync(cancellationToken);
    }
}
