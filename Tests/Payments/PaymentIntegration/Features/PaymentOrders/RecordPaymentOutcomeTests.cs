using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Features.PaymentOrders;
using PaymentSystem.Shared.Messaging;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;

namespace PaymentIntegration.Features.PaymentOrders;

public sealed class RecordPaymentOutcomeTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    private Task<OutgoingMessages> RecordAsync(Guid paymentOrderId, bool succeeded, string reference = "pi_paid") =>
        HandleAsync<RecordPaymentOutcomeHandler, OutgoingMessages>((handler, context, _) =>
            handler.Handle(new RecordPaymentOutcome(paymentOrderId, succeeded, reference), context, CancellationToken.None));

    private Task<List<Wallet>> ReadWalletsAsync(Guid userId) =>
        ReadAsync(c => c.Wallets.Where(w => w.UserId == userId).ToListAsync());

    private Task<List<LedgerEntry>> ReadLedgerAsync() => ReadAsync(c => c.LedgerEntries.ToListAsync());

    [Fact]
    public async Task A_payment_is_settled_in_full_and_the_booking_is_paid()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing, amount: 120.50m);
        var order = checkout.PaymentOrders.Single();

        var outgoing = await RecordAsync(order.PaymentOrderId, succeeded: true, reference: "pi_final");

        var stored = await ReadCheckoutAsync(checkout.BookingId);
        var storedOrder = stored.FindOrder(order.PaymentOrderId);
        Assert.Equal(PaymentOrderStatus.Success, storedOrder.Status);
        Assert.Equal("pi_final", storedOrder.PspTokenRef);
        Assert.True(storedOrder.WalletUpdated);
        Assert.True(storedOrder.LedgerUpdated);
        Assert.True(stored.IsPaymentDone);

        var wallet = Assert.Single(await ReadWalletsAsync(order.SellerId));
        Assert.Equal(120.50m, wallet.CurrentBalance.Amount);
        Assert.Equal("USD", wallet.CurrentBalance.Currency);

        var entries = await ReadLedgerAsync();
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e =>
        {
            Assert.Equal(order.PaymentOrderId, e.TransactionRefId);
            Assert.Equal(RefType.Payment, e.RefType);
            Assert.Equal(120.50m, e.Amount.Amount);
        });
        Assert.Equal(checkout.BuyerId.ToString(), Assert.Single(entries, e => e.EntryType == EntryType.Debit).AccountId);
        Assert.Equal(order.SellerId.ToString(), Assert.Single(entries, e => e.EntryType == EntryType.Credit).AccountId);

        Assert.Equal(checkout.BookingId, Assert.Single(outgoing.OfType<BookingPaidIntegrationEvent>()).BookingId);
    }

    [Fact]
    public async Task The_sellers_existing_wallet_is_credited_rather_than_a_new_one_made()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing, amount: 30m);
        var order = checkout.PaymentOrders.Single();
        await SeedWalletAsync(order.SellerId, 70m);

        await RecordAsync(order.PaymentOrderId, succeeded: true);

        Assert.Equal(100m, Assert.Single(await ReadWalletsAsync(order.SellerId)).CurrentBalance.Amount);
    }

    [Fact]
    public async Task A_repeated_success_credits_nothing_more_and_sends_nothing()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing, amount: 50m);
        var order = checkout.PaymentOrders.Single();
        await RecordAsync(order.PaymentOrderId, succeeded: true);

        var outgoing = await RecordAsync(order.PaymentOrderId, succeeded: true);

        Assert.Empty(outgoing);
        Assert.Equal(50m, Assert.Single(await ReadWalletsAsync(order.SellerId)).CurrentBalance.Amount);
        Assert.Equal(2, (await ReadLedgerAsync()).Count);
    }

    [Fact]
    public async Task A_success_after_the_order_failed_moves_no_money_and_is_left_for_a_person()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Failed, amount: 50m);
        var order = checkout.PaymentOrders.Single();

        var outgoing = await RecordAsync(order.PaymentOrderId, succeeded: true);

        Assert.Empty(outgoing);
        Assert.Equal(PaymentOrderStatus.Failed, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(order.PaymentOrderId).Status);
        Assert.Empty(await ReadWalletsAsync(order.SellerId));
        Assert.Empty(await ReadLedgerAsync());
        Assert.Contains(Logs.Lines, line => line.StartsWith("Error") && line.Contains("manual refund"));
    }

    [Fact]
    public async Task A_success_for_an_order_never_started_changes_nothing()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Init, amount: 50m);
        var order = checkout.PaymentOrders.Single();

        var outgoing = await RecordAsync(order.PaymentOrderId, succeeded: true);

        Assert.Empty(outgoing);
        Assert.Equal(PaymentOrderStatus.Init, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(order.PaymentOrderId).Status);
        Assert.Empty(await ReadLedgerAsync());
    }

    [Fact]
    public async Task An_outcome_for_an_unknown_order_does_nothing()
    {
        var outgoing = await RecordAsync(Guid.NewGuid(), succeeded: true);

        Assert.Empty(outgoing);
        Assert.Empty(await ReadLedgerAsync());
    }

    [Theory]
    [InlineData(OrderState.Init)]
    [InlineData(OrderState.Executing)]
    public async Task A_failure_fails_an_unsettled_order_and_the_payment(OrderState state)
    {
        var checkout = await SeedCheckoutAsync(state);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;

        var outgoing = await RecordAsync(orderId, succeeded: false);

        Assert.Equal(checkout.BookingId, Assert.Single(outgoing.OfType<BookingPaymentFailedIntegrationEvent>()).BookingId);
        Assert.Equal(PaymentOrderStatus.Failed, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(orderId).Status);
    }

    [Fact]
    public async Task A_repeated_failure_sends_nothing()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        await RecordAsync(orderId, succeeded: false);

        var outgoing = await RecordAsync(orderId, succeeded: false);

        Assert.Empty(outgoing);
    }

    [Fact]
    public async Task A_failure_after_the_order_was_paid_changes_nothing()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Success);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;

        var outgoing = await RecordAsync(orderId, succeeded: false);

        Assert.Empty(outgoing);
        var stored = await ReadCheckoutAsync(checkout.BookingId);
        Assert.Equal(PaymentOrderStatus.Success, stored.FindOrder(orderId).Status);
        Assert.True(stored.IsPaymentDone);
    }
}
