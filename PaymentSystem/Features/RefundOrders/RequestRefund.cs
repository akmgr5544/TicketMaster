using Microsoft.EntityFrameworkCore;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;
using TicketMaster.Common.IntegrationEvents;

namespace PaymentSystem.Features.RefundOrders;

// Bookings wants money back for a paid booking. With no amount it is the whole booking; with one, that much off the
// booking's single paid order. Each refund is recorded as Pending; sending it to the PSP is a separate step.
public sealed class RefundRequestedHandler(ILogger<RefundRequestedHandler> logger)
{
    public async Task Handle(RefundRequestedIntegrationEvent message, PaymentDbContext context,
        CancellationToken cancellationToken)
    {
        var checkout = await context.PaymentEvents
            .SingleOrDefaultAsync(e => e.BookingId == message.BookingId, cancellationToken);

        if (checkout is null)
        {
            logger.LogWarning("Refund requested for booking {BookingId}, which has no checkout.", message.BookingId);
            return;
        }

        var paidOrders = checkout.PaymentOrders.Where(o => o.Status == PaymentOrderStatus.Success).ToList();
        var paidOrderIds = paidOrders.Select(o => o.PaymentOrderId).ToList();

        var refunds = await context.RefundOrders
            .Where(r => paidOrderIds.Contains(r.PaymentOrderId))
            .ToListAsync(cancellationToken);

        if (message.Amount is null)
            RefundWholeBooking(message, paidOrders, refunds, context);
        else
            RefundPart(message, message.Amount.Value, paidOrders, refunds, context);
    }

    private static void RefundWholeBooking(RefundRequestedIntegrationEvent message, List<PaymentOrder> paidOrders,
        List<RefundOrder> refunds, PaymentDbContext context)
    {
        foreach (var order in paidOrders)
        {
            // A message sent before refunds had ids carries none; the order's own id still makes a redelivery a no-op.
            var requestId = message.RefundId ?? order.PaymentOrderId;
            if (AlreadyRequested(refunds, order, requestId))
                continue;

            var remaining = order.Amount.Amount - RefundedSoFar(refunds, order);
            if (remaining <= 0)
                continue;

            context.RefundOrders.Add(RefundOrder.Create(
                order.PaymentOrderId, requestId, Money.Create(remaining, order.Amount.Currency), "Booking refunded"));
        }
    }

    private void RefundPart(RefundRequestedIntegrationEvent message, decimal amount, List<PaymentOrder> paidOrders,
        List<RefundOrder> refunds, PaymentDbContext context)
    {
        // Each case below is logged rather than thrown: retrying would not change the answer, so a person has to look.
        if (message.RefundId is not { } requestId)
        {
            logger.LogError("Partial refund for booking {BookingId} carries no refund id; it needs a manual refund.", message.BookingId);
            return;
        }

        if (paidOrders.Count != 1)
        {
            logger.LogError("Partial refund {RefundId} for booking {BookingId} found {Count} paid orders, not one; it needs a manual refund.",
                requestId, message.BookingId, paidOrders.Count);
            return;
        }

        var order = paidOrders[0];
        if (AlreadyRequested(refunds, order, requestId))
            return;

        if (!string.Equals(message.Currency, order.Amount.Currency, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogError("Partial refund {RefundId} is in {Currency}, but booking {BookingId} was paid in {Paid}; it needs a manual refund.",
                requestId, message.Currency, message.BookingId, order.Amount.Currency);
            return;
        }

        var remaining = order.Amount.Amount - RefundedSoFar(refunds, order);
        if (amount > remaining)
        {
            logger.LogError("Partial refund {RefundId} asks for {Amount}, but only {Remaining} of booking {BookingId} is left to refund; it needs a manual refund.",
                requestId, amount, remaining, message.BookingId);
            return;
        }

        try
        {
            context.RefundOrders.Add(RefundOrder.Create(
                order.PaymentOrderId, requestId, Money.Create(amount, order.Amount.Currency), "Seats cancelled"));
        }
        catch (PaymentDomainException exception)
        {
            logger.LogError("Partial refund {RefundId} for booking {BookingId} was refused: {Reason}; it needs a manual refund.",
                requestId, message.BookingId, exception.Message);
        }
    }

    private static bool AlreadyRequested(List<RefundOrder> refunds, PaymentOrder order, Guid requestId) =>
        refunds.Any(r => r.PaymentOrderId == order.PaymentOrderId && r.RequestId == requestId);

    // A failed refund gave nothing back, so it does not count against what is left.
    private static decimal RefundedSoFar(List<RefundOrder> refunds, PaymentOrder order) =>
        refunds
            .Where(r => r.PaymentOrderId == order.PaymentOrderId && r.Status != RefundStatus.Failed)
            .Sum(r => r.RefundAmount.Amount);
}
