using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

public class PaymentEvent
{
    public Guid CheckoutId { get; private set; }
    public long BookingId { get; private set; }
    public Guid BuyerId { get; private set; }
    // A saved method the buyer chose to pay with; a checkout paid with a one-time token on the PSP's page has none.
    public Guid? PaymentMethodId { get; private set; }
    public Money TotalAmount { get; private set; } = null!;
    public bool IsPaymentDone { get; private set; }
    public DateTime CreatedAt { get; private set; }

    // Bumped by every change to the checkout or any of its orders, and checked on save. Two orders settling at once
    // would otherwise each see the other unfinished and leave the checkout never paid; with it, the second save
    // conflicts and its retry decides over the first one's result. Safe to move here because every save loads first.
    public int Version { get; private set; }

    // Encapsulated Domain Child Entity Collection
    private readonly List<PaymentOrder> _paymentOrders = new();
    public IReadOnlyCollection<PaymentOrder> PaymentOrders => _paymentOrders.AsReadOnly();

    private PaymentEvent() { }

    private PaymentEvent(long bookingId, Guid buyerId, string currency)
    {
        CheckoutId = Guid.NewGuid();
        BookingId = bookingId;
        BuyerId = buyerId;
        TotalAmount = Money.Zero(currency);
        IsPaymentDone = false;
        CreatedAt = DateTime.UtcNow;
    }

    public static PaymentEvent Create(long bookingId, Guid buyerId, string currency)
    {
        return new PaymentEvent(bookingId, buyerId, currency);
    }

    public void AddPaymentOrder(Guid sellerId, decimal amount)
    {
        if (IsPaymentDone)
            throw new PaymentDomainException("Cannot add orders to a finalized checkout sequence.");

        // One order per checkout: a booking is for one event, paid to its one organizer. Kept as a collection so the
        // checkout/order split of the design stays, but a second order is refused.
        if (_paymentOrders.Count > 0)
            throw new PaymentDomainException("A checkout has exactly one payment order.");

        var money = Money.Create(amount, TotalAmount.Currency);
        var order = new PaymentOrder(CheckoutId, sellerId, money);
        _paymentOrders.Add(order);

        // Increment running total inside aggregate threshold safely
        TotalAmount += money;
        Version++;
    }

    public void UsePaymentMethod(Guid paymentMethodId)
    {
        if (IsPaymentDone)
            throw new PaymentDomainException("A paid checkout cannot change its payment method.");

        if (PaymentMethodId == paymentMethodId)
            return;

        PaymentMethodId = paymentMethodId;
        Version++;
    }

    // Fails every order still waiting on the buyer. A settled one (paid, failed or refunded) is left alone;
    // returns how many were failed, so a repeated cancellation reports zero.
    public int Cancel()
    {
        var unsettled = _paymentOrders
            .Where(order => order.Status is PaymentOrderStatus.Init or PaymentOrderStatus.Executing)
            .ToList();

        foreach (var order in unsettled)
            order.TransitionToFailed();

        if (unsettled.Count > 0)
            Version++;

        return unsettled.Count;
    }

    // The buyer opened the PSP's page for this order. Asking again through the same provider changes nothing;
    // a settled order cannot be paid again.
    public OrderUpdate StartOrder(Guid paymentOrderId, string provider, string? pspTokenRef, DateTime startedAt)
    {
        var order = FindOrder(paymentOrderId);

        switch (order.Status)
        {
            case PaymentOrderStatus.Init:
                order.StartExecuting(provider, pspTokenRef, startedAt);
                Version++;
                return OrderUpdate.Applied;
            case PaymentOrderStatus.Executing when order.Provider == provider:
                return OrderUpdate.AlreadyApplied;
            case PaymentOrderStatus.Executing:
                throw new PaymentDomainException($"Payment order {paymentOrderId} was started with {order.Provider}.");
            default:
                throw new PaymentDomainException($"Payment order {paymentOrderId} is already {order.Status}.");
        }
    }

    // The PSP took the money. Reported rather than thrown: PSPs repeat themselves and answer late.
    public OrderUpdate SucceedOrder(Guid paymentOrderId, string pspTokenRef)
    {
        var order = FindOrder(paymentOrderId);

        var update = order.Status switch
        {
            PaymentOrderStatus.Executing => OrderUpdate.Applied,
            PaymentOrderStatus.Success or PaymentOrderStatus.Refunded => OrderUpdate.AlreadyApplied,
            PaymentOrderStatus.Failed => OrderUpdate.Superseded,
            _ => OrderUpdate.NotStarted
        };

        if (update == OrderUpdate.Applied)
        {
            order.TransitionToSuccess(pspTokenRef);
            IsPaymentDone = _paymentOrders.All(o => o.Status == PaymentOrderStatus.Success);
            Version++;
        }

        return update;
    }

    // The buyer cancelled on the PSP's page. A paid order does not change its mind.
    public OrderUpdate FailOrder(Guid paymentOrderId)
    {
        var order = FindOrder(paymentOrderId);

        var update = order.Status switch
        {
            PaymentOrderStatus.Init or PaymentOrderStatus.Executing => OrderUpdate.Applied,
            PaymentOrderStatus.Failed => OrderUpdate.AlreadyApplied,
            _ => OrderUpdate.Superseded
        };

        if (update == OrderUpdate.Applied)
        {
            order.TransitionToFailed();
            Version++;
        }

        return update;
    }

    public void MarkOrderSettled(Guid paymentOrderId)
    {
        var order = FindOrder(paymentOrderId);
        order.CompleteWalletDistribution();
        order.CompleteLedgerBookkeeping();
        Version++;
    }

    public void MarkOrderRefunded(Guid paymentOrderId)
    {
        FindOrder(paymentOrderId).TransitionToRefunded();
        Version++;
    }

    public PaymentOrder FindOrder(Guid paymentOrderId) =>
        _paymentOrders.SingleOrDefault(o => o.PaymentOrderId == paymentOrderId)
        ?? throw new PaymentDomainException($"Payment order {paymentOrderId} is not part of checkout {CheckoutId}.");

    public void MarkAsComplete()
    {
        if (_paymentOrders.Count == 0)
        {
            throw new PaymentDomainException("Cannot process a zero-item checkout entry.");
        }
        IsPaymentDone = true;
    }
}
