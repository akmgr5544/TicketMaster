using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Shared;
using PaymentSystem.Enums;
using PaymentSystem.Features.LedgerEntries;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.LedgerEntries;

public sealed class GetOrderLedgerTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task The_ledger_of_an_order_is_its_pay_in_and_every_refund_of_it_in_order()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: 100m, buyerId: buyer);
        var order = checkout.PaymentOrders.Single();
        var refund = await SeedSettledRefundAsync(order.PaymentOrderId, 30m);
        var other = await SeedCheckoutAsync(OrderState.Success, amount: 5m);
        var money = Money.Create(100m, Usd);
        var refunded = Money.Create(30m, Usd);

        await InScopeAsync(async c =>
        {
            c.LedgerEntries.Add(LedgerEntry.Create(order.PaymentOrderId, RefType.Payment, buyer.ToString(), EntryType.Debit, money));
            c.LedgerEntries.Add(LedgerEntry.Create(order.PaymentOrderId, RefType.Payment, order.SellerId.ToString(), EntryType.Credit, money));
            c.LedgerEntries.Add(LedgerEntry.Create(other.PaymentOrders.Single().PaymentOrderId, RefType.Payment, "someone", EntryType.Debit, Money.Create(5m, Usd)));
            c.LedgerEntries.Add(LedgerEntry.Create(refund.RefundOrderId, RefType.Refund, order.SellerId.ToString(), EntryType.Debit, refunded));
            c.LedgerEntries.Add(LedgerEntry.Create(refund.RefundOrderId, RefType.Refund, buyer.ToString(), EntryType.Credit, refunded));
            await c.SaveChangesAsync();
        });

        var result = await Sender.Send(new GetOrderLedger.Query(buyer, order.PaymentOrderId));

        Assert.True(result.IsSuccess);
        var entries = result.Value!;
        Assert.Equal(4, entries.Count);
        Assert.Equal(entries.OrderBy(e => e.EntryId).Select(e => e.EntryId), entries.Select(e => e.EntryId));
        Assert.Equal(["Payment", "Payment", "Refund", "Refund"], entries.Select(e => e.RefType));
        Assert.Equal([100m, 100m, 30m, 30m], entries.Select(e => e.Amount));
        Assert.Equal(["Debit", "Credit", "Debit", "Credit"], entries.Select(e => e.EntryType));
    }

    [Fact]
    public async Task The_seller_sees_the_ledger_too()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success);
        var order = checkout.PaymentOrders.Single();

        var result = await Sender.Send(new GetOrderLedger.Query(order.SellerId, order.PaymentOrderId));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Anyone_else_finds_no_order()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success);

        var result = await Sender.Send(new GetOrderLedger.Query(Guid.NewGuid(), checkout.PaymentOrders.Single().PaymentOrderId));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }
}
