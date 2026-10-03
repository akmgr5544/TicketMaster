using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentSystem.Data;
using PaymentSystem.Domain;
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
    // the PaymentRequested message. The cancellation is recorded, so that request is refused when it lands.
    public sealed record Response(bool Found, int OrdersFailed, int OrdersAlreadyPaid);

    internal sealed class Handler(PaymentDbContext context, ILogger<Handler> logger)
        : IRequestHandler<Command, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            // The checkout is looked for before the claim: one created before claims existed has none.
            if (!await context.PaymentEvents.AnyAsync(e => e.BookingId == request.BookingId, cancellationToken)
                && await ClaimCancellationAsync(request.BookingId, cancellationToken))
                return new Response(false, 0, 0);

            for (var attempt = 0;; attempt++)
            {
                var checkout = await context.PaymentEvents
                    .SingleAsync(e => e.BookingId == request.BookingId, cancellationToken);

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

        // False when a request claimed the booking first: its checkout is committed by now, and is cancelled instead.
        private async Task<bool> ClaimCancellationAsync(long bookingId, CancellationToken cancellationToken)
        {
            var status = await ClaimStatusAsync(bookingId, cancellationToken);
            if (status is null)
            {
                context.BookingClaims.Add(BookingClaim.Cancelled(bookingId));
                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                    logger.LogInformation("Booking {BookingId} was cancelled before its payment was requested; a later request will be refused.",
                        bookingId);
                    return true;
                }
                // A request claimed the booking after the check above; this insert waited for it to commit. EF
                // saved inside a savepoint, so the transaction is still usable.
                catch (DbUpdateException exception)
                    when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
                {
                    context.ChangeTracker.Clear();
                    status = await ClaimStatusAsync(bookingId, cancellationToken);
                }
            }

            return status == BookingClaimStatus.Cancelled;
        }

        private Task<BookingClaimStatus?> ClaimStatusAsync(long bookingId, CancellationToken cancellationToken) =>
            context.BookingClaims
                .Where(c => c.BookingId == bookingId)
                .Select(c => (BookingClaimStatus?)c.Status)
                .SingleOrDefaultAsync(cancellationToken);
    }
}

public sealed class BookingCancelledConsumer(ISender sender)
{
    public async Task Consume(BookingCancelledIntegrationEvent message, CancellationToken cancellationToken) =>
        await sender.Send(new CancelCheckout.Command(message.BookingId), cancellationToken);
}
