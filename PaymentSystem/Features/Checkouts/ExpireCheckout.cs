using MediatR;
using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Pipelines;
using PaymentSystem.Shared.Results;
using Wolverine.Attributes;

namespace PaymentSystem.Features.Checkouts;

// Fails what is still unpaid once the payment window closes, so Bookings is told (through the Fail handler) and
// releases the seats instead of holding them for a buyer who walked away.
public static class ExpireCheckout
{
    // A constant rather than configuration: it is how long Bookings holds the seats for, not a tuning knob.
    public static readonly TimeSpan After = TimeSpan.FromMinutes(15);

    public sealed record Command(Guid CheckoutId) : IRequest<Result<Response>>, ITransactionalRequest;

    public sealed record Response(int OrdersFailed);

    internal sealed class Handler(PaymentDbContext context, ILogger<Handler> logger)
        : IRequestHandler<Command, Result<Response>>
    {
        public async Task<Result<Response>> Handle(Command request, CancellationToken cancellationToken)
        {
            for (var attempt = 0;; attempt++)
            {
                var checkout = await context.PaymentEvents
                    .SingleOrDefaultAsync(e => e.CheckoutId == request.CheckoutId, cancellationToken);
                if (checkout is null)
                {
                    // The timer is scheduled in the transaction that inserts the checkout, so this is not a race.
                    logger.LogWarning("Checkout {CheckoutId} expired but does not exist.", request.CheckoutId);
                    return new Response(0);
                }

                var failed = checkout.Expire();
                if (failed == 0)
                    return new Response(0);

                if (checkout.PaymentOrders.Any(o => o.Status == PaymentOrderStatus.Success))
                    logger.LogWarning(
                        "Checkout {CheckoutId} for booking {BookingId} expired partly paid; the paid orders need reconciling.",
                        checkout.CheckoutId, checkout.BookingId);

                try
                {
                    await context.SaveChangesAsync(cancellationToken);
                    logger.LogInformation("Checkout {CheckoutId} expired; failed {Count} unpaid orders.",
                        checkout.CheckoutId, failed);
                    return new Response(failed);
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

// Pinned: a scheduled envelope sits in the store for the whole window, and a renamed type would strand it.
[MessageIdentity("checkout-expiry-due")]
public sealed record CheckoutExpiryDue(Guid CheckoutId);

public sealed class CheckoutExpiryDueConsumer(ISender sender)
{
    public async Task Consume(CheckoutExpiryDue message, CancellationToken cancellationToken) =>
        await sender.Send(new ExpireCheckout.Command(message.CheckoutId), cancellationToken);
}
