using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

    public class PaymentEvent
    {
        public Guid CheckoutId { get; private set; }
        public long BookingId { get; private set; }
        public Guid BuyerId { get; private set; }
        // Chosen on the PSP's page after the checkout exists, so a new checkout has none.
        public Guid? PaymentMethodId { get; private set; }
        public Money TotalAmount { get; private set; } = null!;
        public bool IsPaymentDone { get; private set; }
        public DateTime CreatedAt { get; private set; }

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
            
            var money = Money.Create(amount, TotalAmount.Currency);
            var order = new PaymentOrder(CheckoutId, sellerId, money);
            _paymentOrders.Add(order);
            
            // Increment running total inside aggregate threshold safely
            TotalAmount += money;
        }

        // Fails every order still waiting on the buyer. A settled one (paid, failed or refunded) is left alone;
        // returns how many were failed, so a repeated cancellation reports zero.
        public int Cancel()
        {
            var unsettled = _paymentOrders
                .Where(order => order.Status is PaymentOrderStatus.Init or PaymentOrderStatus.NotStarted or PaymentOrderStatus.Executing)
                .ToList();

            foreach (var order in unsettled)
                order.TransitionToFailed();

            return unsettled.Count;
        }

        // The buyer opened the PSP's page for this order. Asking again through the same provider changes nothing;
        // a settled order cannot be paid again.
        public OrderUpdate StartOrder(Guid paymentOrderId, string provider, string? pspTokenRef)
        {
            var order = FindOrder(paymentOrderId);

            switch (order.Status)
            {
                case PaymentOrderStatus.Init or PaymentOrderStatus.NotStarted:
                    order.StartExecuting(provider, pspTokenRef);
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
            }

            return update;
        }

        // The buyer cancelled on the PSP's page. A paid order does not change its mind.
        public OrderUpdate FailOrder(Guid paymentOrderId)
        {
            var order = FindOrder(paymentOrderId);

            var update = order.Status switch
            {
                PaymentOrderStatus.Init or PaymentOrderStatus.NotStarted or PaymentOrderStatus.Executing => OrderUpdate.Applied,
                PaymentOrderStatus.Failed => OrderUpdate.AlreadyApplied,
                _ => OrderUpdate.Superseded
            };

            if (update == OrderUpdate.Applied)
                order.TransitionToFailed();

            return update;
        }

        public void MarkOrderSettled(Guid paymentOrderId)
        {
            var order = FindOrder(paymentOrderId);
            order.CompleteWalletDistribution();
            order.CompleteLedgerBookkeeping();
        }

        public void MarkOrderRefunded(Guid paymentOrderId) => FindOrder(paymentOrderId).TransitionToRefunded();

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