using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Results;
using TicketMaster.Common.IntegrationEvents;
using Wolverine.Attributes;

namespace PaymentSystem.Features.Checkouts;

// Gives back what a booking paid: everything still paid on every order (no amount), or one amount off its single
// paid order — the seats a customer cancelled.
public static class RefundCheckout
{
    // Deliberately not transactional, for SubmitPaymentMethod's reason: a refund is a call to another system that
    // can take seconds, so it runs with no transaction open and RecordRefund does the writing. Safe to run twice —
    // the provider answers a repeated refund with the first one, and RecordRefund reverses the money once.
    public sealed record Command(long BookingId, Guid? RefundId = null, decimal? Amount = null, string? Currency = null)
        : IRequest<Result<Response>>;

    // NeedsAttention: the provider refused or failed the refund, or the request cannot be met. Retrying will not
    // change that, so it is logged for a person to settle, and the booking is not reported refunded.
    public sealed record Response(int OrdersRefunded, int NeedsAttention);

    internal sealed class Handler(
        PaymentDbContext context,
        IPaymentGatewayFactory gateways,
        ISender sender,
        ILogger<Handler> logger) : IRequestHandler<Command, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            var checkout = await context.PaymentEvents.AsNoTracking()
                .SingleOrDefaultAsync(e => e.BookingId == request.BookingId, cancellationToken);
            if (checkout is null)
            {
                logger.LogWarning("A refund was asked for booking {BookingId}, which has no checkout.", request.BookingId);
                return new Response(0, 0);
            }

            return request.Amount is { } amount
                ? await RefundPartAsync(checkout, request, amount, cancellationToken)
                : await RefundEverythingAsync(checkout, request, cancellationToken);
        }

        // Each order gives back what is left on it. Without an id from Bookings the order's own id names the part —
        // stable across redeliveries, and the id refunds made before parts existed were given.
        private async Task<Response> RefundEverythingAsync(PaymentEvent checkout, Command request,
            CancellationToken cancellationToken)
        {
            var refunded = 0;
            var needsAttention = 0;
            foreach (var order in checkout.PaymentOrders.Where(o => o.Status == PaymentOrderStatus.Success))
            {
                var part = request.RefundId ?? order.PaymentOrderId;
                if (await RefundAsync(checkout, order, part, order.RefundableAmount, request.RefundId, wholeCheckout: true,
                        cancellationToken))
                    refunded++;
                else
                    needsAttention++;
            }

            return new Response(refunded, needsAttention);
        }

        // Bookings never puts two sellers in one booking, so a partial refund has exactly one paid order to come off.
        private async Task<Response> RefundPartAsync(PaymentEvent checkout, Command request, decimal amount,
            CancellationToken cancellationToken)
        {
            if (request.RefundId is not { } refundId)
            {
                logger.LogError("A partial refund of booking {BookingId} came without a refund id; it needs a manual refund.",
                    request.BookingId);
                return new Response(0, 1);
            }

            // Recorded already: a redelivery, whose BookingRefunded went out with the record.
            if (checkout.PaymentOrders.Any(o => o.Refunds.Any(r => r.RefundId == refundId)))
                return new Response(0, 0);

            var paid = checkout.PaymentOrders.Where(o => o.Status == PaymentOrderStatus.Success).ToList();
            if (paid.Count == 0)
            {
                // A whole-booking refund got there first and gave everything back, this part included.
                logger.LogWarning("Refund {RefundId} of booking {BookingId} found nothing left to refund.", refundId,
                    request.BookingId);
                return new Response(0, 0);
            }

            if (paid is not [var order] || order.Currency != request.Currency || amount > order.RefundableAmount)
            {
                logger.LogError(
                    "Refund {RefundId} of {Amount} {Currency} for booking {BookingId} does not fit its paid orders; it needs a manual refund.",
                    refundId, amount, request.Currency, request.BookingId);
                return new Response(0, 1);
            }

            return await RefundAsync(checkout, order, refundId, amount, refundId, wholeCheckout: false, cancellationToken)
                ? new Response(1, 0)
                : new Response(0, 1);
        }

        private async Task<bool> RefundAsync(PaymentEvent checkout, PaymentOrder order, Guid refundId, decimal amount,
            Guid? bookingRefundId, bool wholeCheckout, CancellationToken cancellationToken)
        {
            RefundResult refund;
            try
            {
                refund = await gateways.ForProvider(order.Provider).RefundAsync(
                    new RefundRequest(order.PaymentOrderId, order.PspToken, amount, order.Currency, refundId),
                    cancellationToken);
            }
            // Transient faults are left to propagate, so the message's retry policy asks again later.
            catch (PaymentProviderException exception) when (exception.Kind == PaymentProviderErrorKind.InvalidRequest)
            {
                logger.LogError(exception,
                    "The provider refused to refund payment order {PaymentOrderId} of booking {BookingId}; it needs a manual refund.",
                    order.PaymentOrderId, checkout.BookingId);
                return false;
            }

            if (refund.Status == RefundStatus.Failed)
            {
                logger.LogError(
                    "Refund {RefundReference} of payment order {PaymentOrderId} failed ({Reason}); it needs a manual refund.",
                    refund.ProviderReference, order.PaymentOrderId, refund.FailureReason);
                return false;
            }

            // Pending is recorded as refunded: the provider has taken the instruction and holds the money for the
            // buyer. That it can still fail later is a known gap (no refund webhook is handled).
            var recorded = await sender.Send(
                new RecordRefund.Command(order.PaymentOrderId, refundId, amount, refund.ProviderReference, bookingRefundId,
                    wholeCheckout),
                cancellationToken);
            if (!recorded.IsSuccess)
            {
                logger.LogError("Refund {RefundReference} of payment order {PaymentOrderId} went through but was not recorded: {Error}.",
                    refund.ProviderReference, order.PaymentOrderId, recorded.Error!.Code);
                return false;
            }

            return true;
        }
    }
}

public sealed class RefundRequestedConsumer(ISender sender)
{
    public async Task Consume(RefundRequestedIntegrationEvent message, CancellationToken cancellationToken) =>
        await sender.Send(new RefundCheckout.Command(message.BookingId, message.RefundId, message.Amount, message.Currency),
            cancellationToken);
}

// Sent by CancelCheckout to refund a booking cancelled after it was paid. A local message rather than a direct call:
// the cancellation runs inside a transaction, and a refund must not call the provider with one open.
[MessageIdentity("checkout-refund-due")]
public sealed record CheckoutRefundDue(long BookingId);

public sealed class CheckoutRefundDueConsumer(ISender sender)
{
    public async Task Consume(CheckoutRefundDue message, CancellationToken cancellationToken) =>
        await sender.Send(new RefundCheckout.Command(message.BookingId), cancellationToken);
}
