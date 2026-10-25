using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace PaymentSystem.Features.Checkouts;

// Bookings cancelled a booking: fail what is still unpaid, and refund in full what was already paid.
public sealed class BookingCancelledHandler(ILogger<BookingCancelledHandler> logger)
{
    public async Task<OutgoingMessages> Handle(BookingCancelledIntegrationEvent message, PaymentDbContext context,
        CancellationToken cancellationToken)
    {
        var outgoing = new OutgoingMessages();

        var checkout = await context.PaymentEvents
            .SingleOrDefaultAsync(e => e.BookingId == message.BookingId, cancellationToken);

        // The cancellation overtook the payment request: claim the booking, so the request is refused when it lands.
        // A request racing this insert fails on the same key, and its retry finds the claim.
        if (checkout is null)
        {
            if (!await context.BookingClaims.AnyAsync(c => c.BookingId == message.BookingId, cancellationToken))
            {
                context.BookingClaims.Add(BookingClaim.Cancelled(message.BookingId));
                logger.LogInformation("Booking {BookingId} was cancelled before its payment was requested; a later request will be refused.",
                    message.BookingId);
            }

            return outgoing;
        }

        if (checkout.Cancel() > 0)
            outgoing.Add(new BookingPaymentFailedIntegrationEvent(message.BookingId));

        var paidOrders = checkout.PaymentOrders.Where(o => o.Status == PaymentOrderStatus.Success).ToList();
        var paidOrderIds = paidOrders.Select(o => o.PaymentOrderId).ToList();

        var refunds = await context.RefundOrders
            .Where(r => paidOrderIds.Contains(r.PaymentOrderId))
            .ToListAsync(cancellationToken);

        foreach (var order in paidOrders)
        {
            // Bookings names no refund here, so the order's own id is the request id: a redelivery finds it.
            var requestId = order.PaymentOrderId;
            if (refunds.Any(r => r.PaymentOrderId == order.PaymentOrderId && r.RequestId == requestId))
                continue;

            var refunded = refunds
                .Where(r => r.PaymentOrderId == order.PaymentOrderId && r.Status != RefundOrderStatus.Failed)
                .Sum(r => r.RefundAmount.Amount);

            var remaining = order.Amount.Amount - refunded;
            if (remaining <= 0)
                continue;

            var refund = RefundOrder.Create(
                order.PaymentOrderId, requestId, Money.Create(remaining, order.Amount.Currency), "Booking cancelled");
            context.RefundOrders.Add(refund);
            outgoing.Add(new ProcessRefund(refund.RefundOrderId));
        }

        return outgoing;
    }
}
