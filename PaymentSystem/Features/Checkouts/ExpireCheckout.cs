using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace PaymentSystem.Features.Checkouts;

// The checkout's time is up: fail whatever the buyer has not paid, so Bookings releases the seats. A checkout paid,
// cancelled or already expired has nothing left to fail, and nothing is sent.
public sealed class CheckoutExpiryDueHandler(ILogger<CheckoutExpiryDueHandler> logger)
{
    public async Task<OutgoingMessages> Handle(CheckoutExpiryDue message, PaymentDbContext context,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();

        var checkout = await context.PaymentEvents
            .SingleOrDefaultAsync(e => e.BookingId == message.BookingId, cancellationToken);

        if (checkout is null || checkout.Cancel() == 0)
            return outgoing;

        logger.LogInformation("Checkout for booking {BookingId} expired unpaid.", message.BookingId);
        outgoing.Add(new BookingPaymentFailedIntegrationEvent(message.BookingId));
        return outgoing;
    }
}
