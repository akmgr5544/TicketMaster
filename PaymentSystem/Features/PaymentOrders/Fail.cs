using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Domain.Events;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;

namespace PaymentSystem.Features.PaymentOrders;

public static class Fail
{
    // One failed order voids the whole booking. Two orders failing together publish twice; Bookings'
    // release is idempotent, so that is cheaper than coordinating which of them speaks.
    internal sealed class Handler(PaymentDbContext context, IIntegrationEventPublisher publisher)
        : INotificationHandler<PaymentOrderFailedDomainEvent>
    {
        public async Task Handle(PaymentOrderFailedDomainEvent notification, CancellationToken cancellationToken)
        {
            var bookingId = await context.PaymentEvents
                .Where(e => e.CheckoutId == notification.CheckoutId)
                .Select(e => e.BookingId)
                .SingleAsync(cancellationToken);

            await publisher.PublishAsync(new BookingPaymentFailedIntegrationEvent(bookingId), cancellationToken);
        }
    }
}
