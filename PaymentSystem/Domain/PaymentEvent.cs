using PaymentSystem.Domain.Abstractions;
using PaymentSystem.Domain.Events;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

public class PaymentEvent : Entity
{
    private readonly List<PaymentOrder> _paymentOrders = [];

    public Guid CheckoutId { get; private set; }
    public long BookingId { get; private set; }
    public Guid BuyerId { get; private set; }
    public bool IsPaymentDone { get; private set; }
    public IReadOnlyCollection<PaymentOrder> PaymentOrders => _paymentOrders.AsReadOnly();
    public int OrderCount { get; private set; }
    public int Version { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private PaymentEvent()
    {
    }

    // Holds no order yet; the store refuses a checkout saved without one (OrderCount > 0).
    public static PaymentEvent Create(Guid checkoutId, long bookingId, Guid buyerId)
    {
        if (checkoutId == Guid.Empty)
            throw new PaymentDomainException("A checkout needs an id.");
        if (bookingId <= 0)
            throw new PaymentDomainException("A checkout must pay for a booking.");
        if (buyerId == Guid.Empty)
            throw new PaymentDomainException("A checkout needs a buyer.");

        return new PaymentEvent { CheckoutId = checkoutId, BookingId = bookingId, BuyerId = buyerId };
    }

    public void AddOrder(Guid merchantId, decimal amount, string currency)
    {
        EnsureWhole();
        // An order added once payment began would be one the buyer never saw when they started paying.
        if (_paymentOrders.Exists(order => order.Status != PaymentOrderStatus.NotStarted))
            throw new PaymentDomainException($"Checkout {CheckoutId} takes no new payment order once payment has begun.");

        var order = PaymentOrder.Create(CheckoutId, BuyerId, merchantId, amount, currency);
        // One order per seller: two would double the PSP calls and make a callback ambiguous for that seller.
        if (_paymentOrders.Exists(existing => existing.MerchantId == order.MerchantId))
            throw new PaymentDomainException("A checkout holds one payment order per seller.");

        _paymentOrders.Add(order);
        OrderCount = _paymentOrders.Count;
        RefreshIsPaymentDone();
    }

    public void StartExecuting(Guid paymentOrderId, string provider, string? pspToken)
    {
        OrderById(paymentOrderId).StartExecuting(provider, pspToken);
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

    // True only when this call refunded the order; a repeat returns false so the caller reverses the money once.
    public bool RefundOrder(Guid paymentOrderId, string refundReference)
    {
        var refunded = OrderById(paymentOrderId).Refund(refundReference);
        RefreshIsPaymentDone();
        return refunded;
    }

    // Everything that was paid has gone back, and something was paid to begin with.
    public bool IsFullyRefunded =>
        _paymentOrders.Exists(order => order.Status == PaymentOrderStatus.Refunded)
        && !_paymentOrders.Exists(order => order.Status == PaymentOrderStatus.Success);

    // The provider's final word on an order — a webhook, a synchronous charge or a reconciliation lookup. Reports
    // rather than throws for a repeat, a stale answer or an early one: all three arrive in normal operation, since
    // providers deliver at least once and out of order.
    public OrderUpdate ApplyProviderAnswer(Guid paymentOrderId, bool succeeded)
    {
        var order = OrderById(paymentOrderId);
        switch (order.Status)
        {
            case PaymentOrderStatus.NotStarted:
                return OrderUpdate.NotStarted;
            case PaymentOrderStatus.Executing when succeeded:
                SucceedOrder(paymentOrderId);
                return OrderUpdate.Applied;
            case PaymentOrderStatus.Executing:
                FailOrder(paymentOrderId);
                return OrderUpdate.Applied;
            // The payment stays succeeded at the provider after a refund, so a late success is old news.
            case PaymentOrderStatus.Success or PaymentOrderStatus.Refunded:
                return succeeded ? OrderUpdate.AlreadyApplied : OrderUpdate.Superseded;
            default:
                return succeeded ? OrderUpdate.Superseded : OrderUpdate.AlreadyApplied;
        }
    }

    public PaymentOrder Order(Guid paymentOrderId) => OrderById(paymentOrderId);

    public int Expire() => AbandonUnsettledOrders();

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

    // TrueForAll is true over no orders; a checkout with nothing to pay is not paid.
    private void RefreshIsPaymentDone() =>
        IsPaymentDone = _paymentOrders.Count > 0
                        && _paymentOrders.TrueForAll(order => order.Status == PaymentOrderStatus.Success);

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
