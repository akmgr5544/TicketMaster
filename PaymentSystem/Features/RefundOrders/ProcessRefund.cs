using Microsoft.EntityFrameworkCore;
using PaymentProvider.Abstractions;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Messaging;
using Wolverine.Attributes;

namespace PaymentSystem.Features.RefundOrders;

// Sends a recorded refund to the PSP that took the payment. Non-transactional: a PSP call must not hold a database
// transaction open, so this only reads, and the answer is recorded by RecordRefundOutcome in its own transaction.
// The refund's request id is the PSP's idempotency key, so a retry after a crash replays the same refund.
[NonTransactional]
public sealed class ProcessRefundHandler
{
    public async Task<RecordRefundOutcome?> Handle(ProcessRefund message, PaymentQueryDbContext context,
        IPaymentGatewayFactory gateways, CancellationToken cancellationToken)
    {
        var refund = await context.RefundOrders
            .SingleOrDefaultAsync(r => r.RefundOrderId == message.RefundOrderId, cancellationToken);

        // Already answered: a redelivery.
        if (refund is null || refund.Status != nameof(RefundOrderStatus.Pending))
            return null;

        var order = await context.PaymentOrders
            .SingleAsync(o => o.PaymentOrderId == refund.PaymentOrderId, cancellationToken);

        try
        {
            var result = await gateways.ForProvider(order.Provider).RefundAsync(
                new RefundRequest(order.PaymentOrderId, order.PspTokenRef, refund.RefundAmount, refund.Currency, refund.RequestId),
                cancellationToken);

            return new RecordRefundOutcome(
                refund.RefundOrderId, result.Status != RefundStatus.Failed, result.ProviderReference, result.FailureReason);
        }
        // The PSP refused it; asking again will not change that. A Transient error is not caught: it propagates and
        // the message is retried later.
        catch (PaymentProviderException exception) when (exception.Kind == PaymentProviderErrorKind.InvalidRequest)
        {
            return new RecordRefundOutcome(refund.RefundOrderId, false, null, exception.Message);
        }
    }
}
