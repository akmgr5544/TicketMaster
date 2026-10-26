using Microsoft.EntityFrameworkCore;
using Notifications.Data;
using Notifications.Domain;
using TicketMaster.Common.IntegrationEvents;

namespace Notifications.Features.Bookings;

// Remembers who bought a booking, so its outcomes can be sent to them. A redelivery adds nothing.
public sealed class PaymentRequestedHandler
{
    public async Task Handle(PaymentRequestedIntegrationEvent message, NotificationsDbContext context,
        CancellationToken cancellationToken)
    {
        if (await context.BookingBuyers.AnyAsync(b => b.BookingId == message.BookingId, cancellationToken))
            return;

        context.BookingBuyers.Add(new BookingBuyer(message.BookingId, message.BuyerId));
    }
}
