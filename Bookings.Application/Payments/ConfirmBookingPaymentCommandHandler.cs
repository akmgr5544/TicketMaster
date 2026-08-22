using Bookings.Application.Exceptions;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.Payments;

internal sealed class ConfirmBookingPaymentCommandHandler : IRequestHandler<ConfirmBookingPaymentCommand>
{
    private readonly IBookingRepository _bookings;

    public ConfirmBookingPaymentCommandHandler(IBookingRepository bookings)
    {
        _bookings = bookings;
    }

    /// <summary>
    /// The tickets are left exactly as they are. They were already booked when the booking was made,
    /// and paying for them does not change which seats are taken — it only settles the booking.
    /// </summary>
    public async Task Handle(ConfirmBookingPaymentCommand request, CancellationToken cancellationToken)
    {
        var booking = await _bookings.GetByIdAsync(request.BookingId, cancellationToken);

        if (booking is null)
            throw new BookingException($"Booking {request.BookingId} was not found");

        booking.MarkPaid();
        await _bookings.SaveChangesAsync(cancellationToken);
    }
}
