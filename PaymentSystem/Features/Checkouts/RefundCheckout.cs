using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Results;
using TicketMaster.Common.IntegrationEvents;
using Wolverine.Attributes;

namespace PaymentSystem.Features.Checkouts;

// Gives back everything a booking paid: every successful order of its checkout, in full.
public static class RefundCheckout
{
    // Deliberately not transactional, for SubmitPaymentMethod's reason: a refund is a call to another system that
    // can take seconds, so it runs with no transaction open and RecordRefund does the writing. Safe to run twice —
    // the provider answers a repeated refund with the first one, and RecordRefund reverses the money once.
    public sealed record Command(long BookingId) : IRequest<Result<Response>>;

    // NeedsAttention: the provider refused or failed the refund. Retrying will not change that, so it is logged for
    // a person to settle, and the booking is not reported refunded.
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

            var refunded = 0;
            var needsAttention = 0;
            foreach (var order in checkout.PaymentOrders.Where(o => o.Status == PaymentOrderStatus.Success))
            {
                RefundResult refund;
                try
                {
                    refund = await gateways.ForProvider(order.Provider).RefundAsync(
                        new RefundRequest(order.PaymentOrderId, order.PspToken, order.Amount, order.Currency),
                        cancellationToken);
                }
                // Transient faults are left to propagate, so the message's retry policy asks again later.
                catch (PaymentProviderException exception) when (exception.Kind == PaymentProviderErrorKind.InvalidRequest)
                {
                    logger.LogError(exception,
                        "The provider refused to refund payment order {PaymentOrderId} of booking {BookingId}; it needs a manual refund.",
                        order.PaymentOrderId, request.BookingId);
                    needsAttention++;
                    continue;
                }

                if (refund.Status == RefundStatus.Failed)
                {
                    logger.LogError(
                        "Refund {RefundReference} of payment order {PaymentOrderId} failed ({Reason}); it needs a manual refund.",
                        refund.ProviderReference, order.PaymentOrderId, refund.FailureReason);
                    needsAttention++;
                    continue;
                }

                // Pending is recorded as refunded: the provider has taken the instruction and holds the money for the
                // buyer. That it can still fail later is a known gap (no refund webhook is handled).
                var recorded = await sender.Send(new RecordRefund.Command(order.PaymentOrderId, refund.ProviderReference),
                    cancellationToken);
                if (!recorded.IsSuccess)
                {
                    logger.LogError("Refund {RefundReference} of payment order {PaymentOrderId} went through but was not recorded: {Error}.",
                        refund.ProviderReference, order.PaymentOrderId, recorded.Error!.Code);
                    needsAttention++;
                    continue;
                }

                refunded++;
            }

            return new Response(refunded, needsAttention);
        }
    }
}

public sealed class RefundRequestedConsumer(ISender sender)
{
    public async Task Consume(RefundRequestedIntegrationEvent message, CancellationToken cancellationToken) =>
        await sender.Send(new RefundCheckout.Command(message.BookingId), cancellationToken);
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
