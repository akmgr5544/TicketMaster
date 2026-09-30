using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

// Part of the PaymentEvent aggregate: created and changed only through its checkout, which is why every
// mutator is internal. Each transition reports whether it changed anything, so the root raises an event
// only for a real transition — never for a redelivered no-op.
public class PaymentOrder
{
    public const int PspTokenMaxLength = 200;

    // Also the idempotency key sent to the PSP, so it is minted once here and never regenerated.
    public Guid PaymentOrderId { get; private set; }
    public Guid CheckoutId { get; private set; }
    // Duplicated from the checkout so the ledger's debit side can be written from the order alone.
    public Guid BuyerId { get; private set; }
    public Guid MerchantId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public PaymentOrderStatus Status { get; private set; }
    public string? PspToken { get; private set; }
    public bool WalletUpdated { get; private set; }
    public bool LedgerUpdated { get; private set; }
    // Stamped by AuditTimestampsInterceptor on save.
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private PaymentOrder()
    {
    }

    internal static PaymentOrder Create(Guid checkoutId, Guid buyerId, PaymentOrderLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.MerchantId == Guid.Empty)
            throw new PaymentDomainException("A payment order needs a merchant.");
        // The debit and the credit would land on the same account: money that moves nowhere, yet is recorded.
        if (line.MerchantId == buyerId)
            throw new PaymentDomainException("A buyer cannot pay themselves.");
        MoneyAmount.EnsurePositiveAndStorable(line.Amount, "A payment order amount");
        CurrencyCode.EnsureValid(line.Currency);

        return new PaymentOrder
        {
            PaymentOrderId = Guid.CreateVersion7(),
            CheckoutId = checkoutId,
            BuyerId = buyerId,
            MerchantId = line.MerchantId,
            Amount = line.Amount,
            Currency = line.Currency,
            Status = PaymentOrderStatus.NotStarted
        };
    }

    // Null is a provider that creates nothing until a payment method is submitted (Braintree); there is no
    // reference to record yet, and the order id alone correlates its later outcome. A reference that is
    // supplied must still be a real one. Starting again with the reference already recorded is a replay of
    // the same start (null included); a different one would be a second payment and is refused.
    internal bool StartExecuting(string? pspToken)
    {
        if (pspToken is not null && string.IsNullOrWhiteSpace(pspToken))
            throw new PaymentDomainException("A PSP token cannot be blank.");
        if (pspToken?.Length > PspTokenMaxLength)
            throw new PaymentDomainException($"A PSP token cannot be longer than {PspTokenMaxLength} characters.");
        if (Status == PaymentOrderStatus.Executing && PspToken == pspToken)
            return false;
        if (Status != PaymentOrderStatus.NotStarted)
            throw new PaymentDomainException($"Cannot start a payment order that is {Status}.");

        PspToken = pspToken;
        Status = PaymentOrderStatus.Executing;
        return true;
    }

    // PSP callbacks are redelivered, so repeating the outcome already reached is a no-op. The opposite
    // outcome is refused: a settled payment cannot change its mind.
    internal bool Succeed()
    {
        if (Status == PaymentOrderStatus.Success)
            return false;
        EnsureExecuting();

        Status = PaymentOrderStatus.Success;
        return true;
    }

    internal bool Fail()
    {
        if (Status == PaymentOrderStatus.Failed)
            return false;
        EnsureExecuting();

        Status = PaymentOrderStatus.Failed;
        return true;
    }

    // The checkout was abandoned — it expired, or its booking was cancelled — before this order settled. Unlike
    // Fail, it also applies to an order never started. A settled order is left alone: a success already moved
    // money, which only a reconciliation can undo.
    internal bool Abandon()
    {
        if (Status is not (PaymentOrderStatus.NotStarted or PaymentOrderStatus.Executing))
            return false;

        Status = PaymentOrderStatus.Failed;
        return true;
    }

    internal bool MarkWalletUpdated()
    {
        EnsureSucceeded();
        if (WalletUpdated)
            return false;

        WalletUpdated = true;
        return true;
    }

    internal bool MarkLedgerUpdated()
    {
        EnsureSucceeded();
        if (LedgerUpdated)
            return false;

        LedgerUpdated = true;
        return true;
    }

    private void EnsureExecuting()
    {
        if (Status != PaymentOrderStatus.Executing)
            throw new PaymentDomainException($"Cannot settle a payment order that is {Status}.");
    }

    private void EnsureSucceeded()
    {
        if (Status != PaymentOrderStatus.Success)
            throw new PaymentDomainException("Only a successful payment order moves money.");
    }
}
