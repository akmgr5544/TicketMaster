using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Shared.Messaging;
using PaymentSystem.Shared.Pipelines;
using PaymentSystem.Shared.Psp;
using PaymentSystem.Shared.Results;
using TicketMaster.Common.IntegrationEvents;

namespace PaymentSystem.Features.Checkouts;

// The write half of a refund the provider has already accepted: the order, the seller's wallet, the ledger pair
// and BookingRefunded commit together, so a refund is never recorded in part.
public static class RecordRefund
{
    public sealed record Command(Guid PaymentOrderId, string RefundReference) : IRequest<Result<Response>>, ITransactionalRequest;

    // Recorded false: the order was refunded already — a redelivery, which reverses nothing a second time.
    public sealed record Response(bool Recorded, bool BookingRefunded);

    internal sealed class Handler(PaymentDbContext context, IIntegrationEventPublisher publisher)
        : IRequestHandler<Command, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            for (var attempt = 0;; attempt++)
            {
                var checkout = await ProviderOutcome.FindCheckoutAsync(context, request.PaymentOrderId, null, cancellationToken);
                if (checkout is null)
                    return Error.NotFound("payment_order_not_found", $"No payment order {request.PaymentOrderId}.");

                if (!checkout.RefundOrder(request.PaymentOrderId, request.RefundReference))
                    return new Response(false, false);

                var order = ProviderOutcome.OrderIn(checkout, request.PaymentOrderId);
                // Settlement created it when the order succeeded, and Refund refuses an order whose wallet was never
                // credited, so it is there.
                var wallet = await context.Wallets.SingleAsync(
                    w => w.OwnerId == order.MerchantId && w.Currency == order.Currency, cancellationToken);
                wallet.Debit(order.Amount, order.Currency);
                context.LedgerEntries.AddRange(LedgerEntry.RecordRefund(order));

                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                }
                // A refund of a sibling order, or a wallet credit for this seller, landed first. Decided again over
                // the fresh state, once; a second loss goes back to the message's retry policy.
                catch (DbUpdateConcurrencyException) when (attempt == 0)
                {
                    context.ChangeTracker.Clear();
                    continue;
                }

                // Staged after the save, as Settle does, so a lost race cannot leave a copy behind in the outbox.
                // Only the refund of the last paid order finds the checkout fully refunded, so it goes out once.
                var bookingRefunded = checkout.IsFullyRefunded;
                if (bookingRefunded)
                    await publisher.PublishAsync(new BookingRefundedIntegrationEvent(checkout.BookingId), cancellationToken);

                return new Response(true, bookingRefunded);
            }
        }
    }
}
