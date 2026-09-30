using Microsoft.EntityFrameworkCore;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Shared.Results;

namespace PaymentSystem.Shared.Psp;

// Shared by the slices that talk to the PSP: StartCheckout, SubmitPaymentMethod and HandleWebhook. A
// synchronous charge result and a webhook are the same news from the provider, so they go through one
// mapping and cannot drift apart.
internal static class ProviderOutcome
{
    public enum Kind
    {
        Applied,
        // The order is already where this status would take it: a redelivery.
        AlreadyApplied,
        // Not a final status for the order — Failed included, since the customer can still retry.
        NotFinal,
        // The order already settled the other way; a settled payment does not change its mind.
        Superseded,
        // The provider reports on an order this service has not started; retrying later can succeed.
        NotStarted,
        OrderNotFound,
        // Lost a race with another write to the same checkout twice in a row.
        Conflict
    }

    public sealed record Applied(Kind Kind, PaymentOrderStatus? OrderStatus);

    // The buyer is in the predicate when there is one, so another buyer's order is indistinguishable from a
    // missing one. The root comes with all its orders (AutoInclude), which the checkout-wide rule needs.
    public static Task<PaymentEvent?> FindCheckoutAsync(PaymentDbContext context, Guid paymentOrderId,
        Guid? buyerId, CancellationToken cancellationToken) =>
        context.PaymentEvents
            .Where(e => e.PaymentOrders.Any(o => o.PaymentOrderId == paymentOrderId))
            .Where(e => buyerId == null || e.BuyerId == buyerId)
            .SingleOrDefaultAsync(cancellationToken);

    public static PaymentOrder OrderIn(PaymentEvent checkout, Guid paymentOrderId) =>
        checkout.PaymentOrders.Single(o => o.PaymentOrderId == paymentOrderId);

    // Only Succeeded and Canceled move the order; every other status leaves it Executing. Retries a lost
    // concurrency race once, from a cleared change tracker: reloading only the root would keep the sibling
    // orders it decides "all succeeded" over stale. The same context is kept on purpose — a fresh scope would
    // write outside the request's transaction, and so would the settlement handlers its save dispatches.
    public static async Task<Applied> ApplyAsync(PaymentDbContext context, ILogger logger, Guid paymentOrderId,
        PaymentStatus status, CancellationToken cancellationToken)
    {
        if (status is not (PaymentStatus.Succeeded or PaymentStatus.Canceled))
            return new Applied(Kind.NotFinal, null);

        for (var attempt = 0;; attempt++)
        {
            var checkout = await FindCheckoutAsync(context, paymentOrderId, null, cancellationToken);
            if (checkout is null)
                return new Applied(Kind.OrderNotFound, null);

            var order = OrderIn(checkout, paymentOrderId);
            var decision = Decide(order.Status, status);
            if (decision != Kind.Applied)
            {
                if (decision == Kind.Superseded)
                    logger.LogWarning(
                        "Payment order {PaymentOrderId} is {OrderStatus} but the provider reports {ProviderStatus}; it needs reconciling.",
                        paymentOrderId, order.Status, status);
                return new Applied(decision, order.Status);
            }

            if (status == PaymentStatus.Succeeded)
                checkout.SucceedOrder(paymentOrderId);
            else
                checkout.FailOrder(paymentOrderId);

            try
            {
                await context.SaveChangesAsync(cancellationToken);
                return new Applied(Kind.Applied, order.Status);
            }
            // Events still pending means this write itself was refused. Once they are cleared the refusal came
            // from a settlement handler's own save inside the dispatch, after this write landed — retrying would
            // find the order settled, call it a redelivery, and commit it without its settlement.
            catch (DbUpdateConcurrencyException) when (checkout.DomainEvents.Length > 0)
            {
                if (attempt > 0)
                    return new Applied(Kind.Conflict, null);

                context.ChangeTracker.Clear();
            }
            catch (DbUpdateConcurrencyException)
            {
                return new Applied(Kind.Conflict, null);
            }
        }
    }

    // Only the two kinds a caller can act on are translated; a misconfigured or misbehaving provider is this
    // service's fault and surfaces as a 500 rather than as a client error.
    public static bool IsCallerFacing(PaymentProviderException exception) =>
        exception.Kind is PaymentProviderErrorKind.InvalidRequest or PaymentProviderErrorKind.Transient;

    public static Error ToError(PaymentProviderException exception) =>
        exception.Kind == PaymentProviderErrorKind.Transient
            ? Error.Conflict("psp_unavailable", "The payment provider is unavailable. Try again.")
            : Error.BadRequest("psp_rejected_request", "The payment provider rejected the request.");

    private static Kind Decide(PaymentOrderStatus current, PaymentStatus reported) =>
        (current, reported) switch
        {
            (PaymentOrderStatus.NotStarted, _) => Kind.NotStarted,
            (PaymentOrderStatus.Executing, _) => Kind.Applied,
            (PaymentOrderStatus.Success, PaymentStatus.Succeeded) => Kind.AlreadyApplied,
            (PaymentOrderStatus.Failed, PaymentStatus.Canceled) => Kind.AlreadyApplied,
            _ => Kind.Superseded
        };
}
