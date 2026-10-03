using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;

namespace PaymentSystem.Domain;

public class PaymentOrder
{
    public const int PspTokenMaxLength = 200;
    public const int ProviderMaxLength = 20;
    public const int RefundReferenceMaxLength = 200;

    public Guid PaymentOrderId { get; private set; }
    public Guid CheckoutId { get; private set; }
    public Guid BuyerId { get; private set; }
    public Guid MerchantId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public PaymentOrderStatus Status { get; private set; }
    public string? Provider { get; private set; }
    public string? PspToken { get; private set; }
    public bool WalletUpdated { get; private set; }
    public bool LedgerUpdated { get; private set; }
    public string? RefundReference { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private PaymentOrder()
    {
    }

    internal static PaymentOrder Create(Guid checkoutId, Guid buyerId, Guid merchantId, decimal amount,
        string currency)
    {
        if (merchantId == Guid.Empty)
            throw new PaymentDomainException("A payment order needs a merchant.");
        // The debit and the credit would land on the same account: money that moves nowhere, yet is recorded.
        if (merchantId == buyerId)
            throw new PaymentDomainException("A buyer cannot pay themselves.");
        MoneyAmount.EnsurePositiveAndStorable(amount, "A payment order amount");
        CurrencyCode.EnsureValid(currency);

        return new PaymentOrder
        {
            PaymentOrderId = Guid.CreateVersion7(),
            CheckoutId = checkoutId,
            BuyerId = buyerId,
            MerchantId = merchantId,
            Amount = amount,
            Currency = currency,
            Status = PaymentOrderStatus.NotStarted
        };
    }

    internal bool StartExecuting(string provider, string? pspToken)
    {
        if (string.IsNullOrWhiteSpace(provider))
            throw new PaymentDomainException("A payment order needs the provider it is started with.");
        if (provider.Length > ProviderMaxLength)
            throw new PaymentDomainException($"A provider name cannot be longer than {ProviderMaxLength} characters.");
        if (pspToken is not null && string.IsNullOrWhiteSpace(pspToken))
            throw new PaymentDomainException("A PSP token cannot be blank.");
        if (pspToken?.Length > PspTokenMaxLength)
            throw new PaymentDomainException($"A PSP token cannot be longer than {PspTokenMaxLength} characters.");
        if (Status == PaymentOrderStatus.Executing && Provider == provider && PspToken == pspToken)
            return false;
        if (Status != PaymentOrderStatus.NotStarted)
            throw new PaymentDomainException($"Cannot start a payment order that is {Status}.");

        Provider = provider;
        PspToken = pspToken;
        Status = PaymentOrderStatus.Executing;
        return true;
    }

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

    internal bool Abandon()
    {
        if (Status is not (PaymentOrderStatus.NotStarted or PaymentOrderStatus.Executing))
            return false;

        Status = PaymentOrderStatus.Failed;
        return true;
    }

    // Only a settled success is refunded: its wallet credit and ledger pair exist, so the refund has something to
    // reverse. A repeat is a no-op, so a redelivered refund cannot reverse the money twice.
    internal bool Refund(string refundReference)
    {
        if (string.IsNullOrWhiteSpace(refundReference))
            throw new PaymentDomainException("A refund needs the provider's reference for it.");
        if (refundReference.Length > RefundReferenceMaxLength)
            throw new PaymentDomainException($"A refund reference cannot be longer than {RefundReferenceMaxLength} characters.");
        if (Status == PaymentOrderStatus.Refunded)
            return false;
        EnsureSucceeded();
        if (!WalletUpdated || !LedgerUpdated)
            throw new PaymentDomainException("A payment order is refunded only once its settlement is recorded.");

        Status = PaymentOrderStatus.Refunded;
        RefundReference = refundReference;
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
