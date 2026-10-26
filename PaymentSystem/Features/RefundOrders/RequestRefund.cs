using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace PaymentSystem.Features.RefundOrders;

// Bookings wants a paid booking's money back — all of it; refunds are whole only. The refund is recorded as Pending,
// and ProcessRefund sends it to the PSP once the record has committed.
public sealed class RefundRequestedHandler(ILogger<RefundRequestedHandler> logger)
{
    public async Task<OutgoingMessages> Handle(RefundRequestedIntegrationEvent message, PaymentDbContext context,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();

        var checkout = await context.PaymentEvents
            .SingleOrDefaultAsync(e => e.BookingId == message.BookingId, cancellationToken);

        if (checkout is null)
        {
            logger.LogWarning("Refund requested for booking {BookingId}, which has no checkout.", message.BookingId);
            return outgoing;
        }

        // A checkout has one order; only a paid one has anything to give back.
        var order = checkout.PaymentOrders.SingleOrDefault(o => o.Status == PaymentOrderStatus.Success);
        if (order is null)
        {
            logger.LogWarning("Refund requested for booking {BookingId}, but nothing of it is paid.", message.BookingId);
            return outgoing;
        }

        // A message sent before refunds had ids carries none; the order's own id still makes a redelivery a no-op.
        var requestId = message.RefundId ?? order.PaymentOrderId;

        var refunds = await context.RefundOrders
            .Where(r => r.PaymentOrderId == order.PaymentOrderId)
            .ToListAsync(cancellationToken);

        if (refunds.Any(r => r.RequestId == requestId))
            return outgoing;

        // A failed refund gave nothing back, so it does not count against what is left.
        var remaining = order.Amount.Amount - refunds.Where(r => r.Status != RefundOrderStatus.Failed).Sum(r => r.RefundAmount.Amount);
        if (remaining <= 0)
            return outgoing;

        var refund = RefundOrder.Create(
            order.PaymentOrderId, requestId, Money.Create(remaining, order.Amount.Currency), "Booking refunded");
        context.RefundOrders.Add(refund);
        outgoing.Add(new ProcessRefund(refund.RefundOrderId));

        return outgoing;
    }
}
