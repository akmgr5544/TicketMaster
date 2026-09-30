using PaymentSystem.Domain.Abstractions;
using PaymentSystem.Domain.Events;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

// A checkout: the aggregate root, owning one payment order per seller. Every change to an order goes
// through here, so the checkout-wide rule — done once every order has succeeded — cannot be bypassed.
public class PaymentEvent : Entity
{
    private readonly List<PaymentOrder> _paymentOrders = [];

    public Guid CheckoutId { get; private set; }
    // The booking this checkout pays for. Unique in the store, which is what turns a redelivered
    // PaymentRequested message into a duplicate-key refusal instead of a second checkout.
    public long BookingId { get; private set; }
    public Guid BuyerId { get; private set; }
    // Stored so queries can filter on it, but always re-derived from the orders at the end of each operation,
    // so a stale copy (a reload of the root alone, a redelivered no-op) cannot leave it saying the wrong thing.
    public bool IsPaymentDone { get; private set; }
    public IReadOnlyCollection<PaymentOrder> PaymentOrders => _paymentOrders.AsReadOnly();
    // How many orders the checkout was created with; orders are never added or removed afterwards. A root
    // handed fewer (IgnoreAutoIncludes plus a filtered Include) refuses every operation rather than deciding
    // "every order succeeded" over part of the list.
    public int OrderCount { get; private set; }
    // Concurrency token, bumped once per save by AggregateVersionInterceptor when the checkout or any of its
    // orders changed — never here. Two orders settling at once would otherwise each see the other unfinished
    // and leave the checkout never marked done; with it, the second save is refused and must reload.
    public int Version { get; private set; }
    // Stamped by AuditTimestampsInterceptor on save.
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private PaymentEvent()
    {
    }

    public static PaymentEvent Create(Guid checkoutId, long bookingId, Guid buyerId,
        IReadOnlyCollection<PaymentOrderLine> lines)
    {
        if (checkoutId == Guid.Empty)
            throw new PaymentDomainException("A checkout needs an id.");
        if (bookingId <= 0)
            throw new PaymentDomainException("A checkout must pay for a booking.");
        if (buyerId == Guid.Empty)
            throw new PaymentDomainException("A checkout needs a buyer.");
        ArgumentNullException.ThrowIfNull(lines);
        if (lines.Count == 0)
            throw new PaymentDomainException("A checkout needs at least one payment order.");

        var paymentEvent = new PaymentEvent
        {
            CheckoutId = checkoutId,
            BookingId = bookingId,
            BuyerId = buyerId,
            OrderCount = lines.Count
        };
        paymentEvent._paymentOrders.AddRange(lines.Select(line => PaymentOrder.Create(checkoutId, buyerId, line)));

        // One order per seller: two would double the PSP calls and make a callback ambiguous for that seller.
        if (paymentEvent._paymentOrders.DistinctBy(order => order.MerchantId).Count() != paymentEvent._paymentOrders.Count)
            throw new PaymentDomainException("A checkout holds one payment order per seller.");

        return paymentEvent;
    }

    public void StartExecuting(Guid paymentOrderId, string? pspToken)
    {
        OrderById(paymentOrderId).StartExecuting(pspToken);
        RefreshIsPaymentDone();
    }

    public void SucceedOrder(Guid paymentOrderId)
    {
        var order = OrderById(paymentOrderId);
        if (order.Succeed())
            AddDomainEvent(new PaymentOrderSucceededDomainEvent(order.PaymentOrderId, CheckoutId));
        RefreshIsPaymentDone();
    }

    public void FailOrder(Guid paymentOrderId)
    {
        var order = OrderById(paymentOrderId);
        if (order.Fail())
            AddDomainEvent(new PaymentOrderFailedDomainEvent(order.PaymentOrderId, CheckoutId));
        RefreshIsPaymentDone();
    }

    // The checkout's payment window closed. Returns how many orders it failed; none on a repeat.
    public int Expire() => AbandonUnsettledOrders();

    // The booking was cancelled. Returns how many orders it failed; an order that already succeeded is not
    // reversed here, the caller has to flag it for a refund.
    public int Cancel() => AbandonUnsettledOrders();

    public void MarkWalletUpdated(Guid paymentOrderId)
    {
        OrderById(paymentOrderId).MarkWalletUpdated();
        RefreshIsPaymentDone();
    }

    public void MarkLedgerUpdated(Guid paymentOrderId)
    {
        OrderById(paymentOrderId).MarkLedgerUpdated();
        RefreshIsPaymentDone();
    }

    private int AbandonUnsettledOrders()
    {
        EnsureWhole();
        var abandoned = 0;
        foreach (var order in _paymentOrders.Where(order => order.Abandon()))
        {
            AddDomainEvent(new PaymentOrderFailedDomainEvent(order.PaymentOrderId, CheckoutId));
            abandoned++;
        }

        RefreshIsPaymentDone();
        return abandoned;
    }

    private void RefreshIsPaymentDone() =>
        IsPaymentDone = _paymentOrders.TrueForAll(order => order.Status == PaymentOrderStatus.Success);

    private void EnsureWhole()
    {
        if (_paymentOrders.Count != OrderCount)
            throw new PaymentDomainException(
                $"Checkout {CheckoutId} was loaded with {_paymentOrders.Count} of its {OrderCount} payment orders.");
    }

    private PaymentOrder OrderById(Guid paymentOrderId)
    {
        EnsureWhole();
        return _paymentOrders.Find(order => order.PaymentOrderId == paymentOrderId)
               ?? throw new PaymentDomainException($"Payment order {paymentOrderId} does not belong to checkout {CheckoutId}.");
    }
}
