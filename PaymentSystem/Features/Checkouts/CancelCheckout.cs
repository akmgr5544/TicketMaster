using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Pipelines;
using PaymentSystem.Shared.Results;
using TicketMaster.Common.IntegrationEvents;

namespace PaymentSystem.Features.Checkouts;

// Payments is pay-in only: an order that already succeeded is left as it is and flagged for a refund by hand.
public static class CancelCheckout
{
    public sealed record Command(long BookingId) : IRequest<Result<Response>>, ITransactionalRequest;

    // Found false: no checkout for the booking — it was never sent for payment, or the cancellation overtook
    // the PaymentRequested message.
    public sealed record Response(bool Found, int OrdersFailed, int OrdersAlreadyPaid);

    internal sealed class Handler(PaymentDbContext context, ILogger<Handler> logger)
        : IRequestHandler<Command, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            for (var attempt = 0;; attempt++)
            {
                var checkout = await context.PaymentEvents
                    .SingleOrDefaultAsync(e => e.BookingId == request.BookingId, cancellationToken);
                if (checkout is null)
                {
                    logger.LogInformation("Booking {BookingId} was cancelled with no checkout to cancel.", request.BookingId);
                    return new Response(false, 0, 0);
                }

                var failed = checkout.Cancel();
                var paid = checkout.PaymentOrders.Count(o => o.Status == PaymentOrderStatus.Success);
                // Logged on every delivery, redeliveries included: nothing records that it was flagged already.
                if (paid > 0)
                    logger.LogWarning(
                        "Booking {BookingId} was cancelled after {Paid} of its payment orders succeeded; checkout {CheckoutId} needs a refund.",
                        request.BookingId, paid, checkout.CheckoutId);

                if (failed == 0)
                    return new Response(true, 0, paid);

                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                    return new Response(true, failed, paid);
                }
                // A PSP outcome for one of its orders landed first. Decided again over the fresh state, once;
                // a second loss goes back to the message's retry policy.
                catch (DbUpdateConcurrencyException) when (attempt == 0)
                {
                    context.ChangeTracker.Clear();
                }
            }
        }
    }
}

public sealed class BookingCancelledConsumer(ISender sender)
{
    public async Task Consume(BookingCancelledIntegrationEvent message, CancellationToken cancellationToken) =>
        await sender.Send(new CancelCheckout.Command(message.BookingId), cancellationToken);
}
