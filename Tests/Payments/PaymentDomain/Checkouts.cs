using PaymentSystem.Domain;
using PaymentSystem.Enums;

namespace PaymentDomain;

internal static class Checkouts
{
    public const string Token = "psp-token";

    public const string Provider = "Stripe";

    public static OrderLine Line(decimal amount = 25.50m, string currency = "USD") => new(Guid.NewGuid(), amount, currency);

    public static PaymentEvent NewCheckout(Guid? checkoutId = null, Guid? buyerId = null) =>
        PaymentEvent.Create(checkoutId ?? Guid.NewGuid(), Random.Shared.NextInt64(1, long.MaxValue), buyerId ?? Guid.NewGuid());

    public static PaymentEvent WithOrders(this PaymentEvent paymentEvent, params OrderLine[] lines)
    {
        foreach (var line in lines)
            paymentEvent.AddOrder(line.MerchantId, line.Amount, line.Currency);
        return paymentEvent;
    }

    public static PaymentEvent WithLines(params OrderLine[] lines) => NewCheckout().WithOrders(lines);

    public static PaymentEvent SingleOrder(decimal amount = 25.50m, string currency = "USD") => WithLines(Line(amount, currency));

    public static PaymentOrder Only(this PaymentEvent paymentEvent) => Assert.Single(paymentEvent.PaymentOrders);

    public static Guid OnlyId(this PaymentEvent paymentEvent) => paymentEvent.Only().PaymentOrderId;

    public static PaymentOrder Order(this PaymentEvent paymentEvent, Guid paymentOrderId) =>
        Assert.Single(paymentEvent.PaymentOrders, order => order.PaymentOrderId == paymentOrderId);

    public static Guid[] Ids(this PaymentEvent paymentEvent) => paymentEvent.PaymentOrders.Select(order => order.PaymentOrderId).ToArray();

    // Drives one order of the checkout to the given status the only legal way: through the root.
    public static void Drive(this PaymentEvent paymentEvent, Guid paymentOrderId, PaymentOrderStatus status)
    {
        if (status == PaymentOrderStatus.NotStarted)
            return;
        paymentEvent.StartExecuting(paymentOrderId, Provider, Token);
        if (status == PaymentOrderStatus.Success)
            paymentEvent.SucceedOrder(paymentOrderId);
        else if (status == PaymentOrderStatus.Failed)
            paymentEvent.FailOrder(paymentOrderId);
    }

    public static PaymentEvent SingleOrderIn(PaymentOrderStatus status)
    {
        var paymentEvent = SingleOrder();
        paymentEvent.Drive(paymentEvent.OnlyId(), status);
        return paymentEvent;
    }

    // Everything observable about a checkout and its orders, so "a refused call changed nothing" is one assertion.
    public static string Capture(PaymentEvent paymentEvent) => string.Join(
        " | ",
        new[]
        {
            $"checkout={paymentEvent.CheckoutId} buyer={paymentEvent.BuyerId} done={paymentEvent.IsPaymentDone} version={paymentEvent.Version}",
            "events=" + string.Join(",", paymentEvent.DomainEvents.Select(domainEvent => domainEvent.ToString()))
        }.Concat(paymentEvent.PaymentOrders.Select(order =>
            $"order={order.PaymentOrderId} checkout={order.CheckoutId} buyer={order.BuyerId} merchant={order.MerchantId} " +
            $"amount={order.Amount} currency={order.Currency} status={order.Status} provider={order.Provider ?? "<null>"} token={order.PspToken ?? "<null>"} " +
            $"wallet={order.WalletUpdated} ledger={order.LedgerUpdated}")));
}

// One seller's order as a test describes it; expanded into PaymentEvent.AddOrder calls.
public sealed record OrderLine(Guid MerchantId, decimal Amount, string Currency);
