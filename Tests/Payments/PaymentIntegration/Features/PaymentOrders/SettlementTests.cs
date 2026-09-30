using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Events;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Enums;
using TicketMaster.Common.IntegrationEvents;

namespace PaymentIntegration.Features.PaymentOrders;

public sealed class SettlementTests(PaymentsFixture fixture) : MessagingTest(fixture)
{
    [Fact]
    public async Task SucceedingTheOnlyOrder_CreditsANewSellerWalletExactly_AndWritesABalancedPair()
    {
        var saved = await SeedCheckoutAsync(CheckoutSeed.Lines(1, amount: 40.25m), OrderState.Executing);
        var order = saved.Order(saved.OrderId(0));

        await ThroughTransactionBehaviorAsync(saved.CheckoutId, c => c.SucceedOrder(order.PaymentOrderId));

        var wallet = await ReadWalletAsync(order.MerchantId);
        Assert.NotNull(wallet);
        Assert.Equal(40.25m, wallet.Balance);

        var ledger = await ReadLedgerAsync(order.PaymentOrderId);
        Assert.Equal(2, ledger.Length);
        var debit = Assert.Single(ledger, e => e.Type == EntryType.Debit);
        var credit = Assert.Single(ledger, e => e.Type == EntryType.Credit);
        Assert.Equal(saved.BuyerId, debit.AccountId);
        Assert.Equal(order.MerchantId, credit.AccountId);
        Assert.All(ledger, e => Assert.Equal(40.25m, e.Amount));
        Assert.All(ledger, e => Assert.Equal("USD", e.Currency));

        var stored = await ReadCheckoutAsync(saved.CheckoutId);
        Assert.True(stored.IsPaymentDone);
        Assert.Equal(PaymentOrderStatus.Success, stored.Order(order.PaymentOrderId).Status);
        Assert.True(stored.Order(order.PaymentOrderId).WalletUpdated);
        Assert.True(stored.Order(order.PaymentOrderId).LedgerUpdated);
    }

    [Fact]
    public async Task SucceedingTheOnlyOrder_PublishesBookingPaidOnce_InsideTheSettlingTransaction()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);

        await ThroughTransactionBehaviorAsync(saved.CheckoutId, c => c.SucceedOrder(saved.OrderId(0)));

        var published = Assert.Single(Outbox.Published);
        Assert.Equal(new BookingPaidIntegrationEvent(saved.BookingId), published.Event);
        Assert.Equal(LastTransactionId, published.TransactionId);
    }

    [Fact]
    public async Task ExistingSellerWallet_IsCredited_NotDuplicated()
    {
        var merchant = Guid.NewGuid();
        var existing = Wallet.Create(merchant, "USD");
        existing.Credit(100m, "USD");
        await InScopeAsync(async c => { c.Wallets.Add(existing); await c.SaveChangesAsync(); });
        var saved = await SeedCheckoutAsync([new PaymentOrderLine(merchant, 25.50m, "USD")], OrderState.Executing);

        await ThroughTransactionBehaviorAsync(saved.CheckoutId, c => c.SucceedOrder(saved.OrderId(0)));

        var wallets = await ReadAsync(c => c.Wallets.Where(w => w.OwnerId == merchant).ToListAsync());
        var wallet = Assert.Single(wallets);
        Assert.Equal(existing.WalletId, wallet.WalletId);
        Assert.Equal(125.50m, wallet.Balance);
    }

    [Fact]
    public async Task WalletIsPerCurrency_ACreditInAnotherCurrencyGetsItsOwnWallet()
    {
        var merchant = Guid.NewGuid();
        var usd = Wallet.Create(merchant, "USD");
        await InScopeAsync(async c => { c.Wallets.Add(usd); await c.SaveChangesAsync(); });
        var saved = await SeedCheckoutAsync([new PaymentOrderLine(merchant, 9.99m, "EUR")], OrderState.Executing);

        await ThroughTransactionBehaviorAsync(saved.CheckoutId, c => c.SucceedOrder(saved.OrderId(0)));

        Assert.Equal(0m, (await ReadWalletAsync(merchant, "USD"))!.Balance);
        Assert.Equal(9.99m, (await ReadWalletAsync(merchant, "EUR"))!.Balance);
    }

    [Fact]
    public async Task TwoOrders_SettledSeparately_BookingPaidOnlyWhenTheSecondSettles()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing, OrderState.Executing);

        await ThroughTransactionBehaviorAsync(saved.CheckoutId, c => c.SucceedOrder(saved.OrderId(0)));
        Assert.Empty(Outbox.Published);
        Assert.False((await ReadCheckoutAsync(saved.CheckoutId)).IsPaymentDone);

        await ThroughTransactionBehaviorAsync(saved.CheckoutId, c => c.SucceedOrder(saved.OrderId(1)));
        var paid = Assert.Single(Outbox.OfType<BookingPaidIntegrationEvent>());
        Assert.Equal(saved.BookingId, paid.BookingId);
        Assert.Single(Outbox.Published);
    }

    [Fact]
    public async Task TwoOrders_SettledInOneSave_BookingPaidPublishedOnce()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing, OrderState.Executing);

        await ThroughTransactionBehaviorAsync(saved.CheckoutId, c =>
        {
            c.SucceedOrder(saved.OrderId(0));
            c.SucceedOrder(saved.OrderId(1));
        });

        Assert.Single(Outbox.Published);
        Assert.Single(Outbox.OfType<BookingPaidIntegrationEvent>());
        var stored = await ReadCheckoutAsync(saved.CheckoutId);
        Assert.All(stored.PaymentOrders, o => Assert.True(o.WalletUpdated && o.LedgerUpdated));
    }

    [Fact]
    public async Task TwoCheckoutsForOneSeller_ShareOneWalletCreditedWithBoth()
    {
        // One checkout holds one order per seller, so a seller's second credit comes from another checkout.
        var merchant = Guid.NewGuid();
        var first = await SeedCheckoutAsync([new PaymentOrderLine(merchant, 10m, "USD")], OrderState.Executing);
        var second = await SeedCheckoutAsync([new PaymentOrderLine(merchant, 2.5m, "USD")], OrderState.Executing);

        await ThroughTransactionBehaviorAsync(first.CheckoutId, c => c.SucceedOrder(first.OrderId(0)));
        await ThroughTransactionBehaviorAsync(second.CheckoutId, c => c.SucceedOrder(second.OrderId(0)));

        Assert.Equal(12.5m, (await ReadWalletAsync(merchant))!.Balance);
        Assert.Equal(1, await ReadAsync(c => c.Wallets.CountAsync(w => w.OwnerId == merchant)));
    }

    [Fact]
    public async Task OneOrderSucceedsAndAnotherFails_NoBookingPaid_AndFailurePublished()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing, OrderState.Executing);

        await ThroughTransactionBehaviorAsync(saved.CheckoutId, c =>
        {
            c.SucceedOrder(saved.OrderId(0));
            c.FailOrder(saved.OrderId(1));
        });

        Assert.Empty(Outbox.OfType<BookingPaidIntegrationEvent>());
        Assert.Equal(saved.BookingId, Assert.Single(Outbox.OfType<BookingPaymentFailedIntegrationEvent>()).BookingId);
        // The succeeded order's money still moved: settlement is per order, the booking outcome is not.
        Assert.True((await ReadOrderAsync(saved.OrderId(0))).WalletUpdated);
    }

    [Fact]
    public async Task RedeliveredSuccess_DoesNotCreditTwice_OrPublishAgain()
    {
        var saved = await SeedCheckoutAsync(CheckoutSeed.Lines(1, amount: 30m), OrderState.Executing);
        var merchant = saved.Order(saved.OrderId(0)).MerchantId;
        await ThroughTransactionBehaviorAsync(saved.CheckoutId, c => c.SucceedOrder(saved.OrderId(0)));

        await ThroughTransactionBehaviorAsync(saved.CheckoutId, c => c.SucceedOrder(saved.OrderId(0)));

        Assert.Equal(30m, (await ReadWalletAsync(merchant))!.Balance);
        Assert.Equal(2, (await ReadLedgerAsync(saved.OrderId(0))).Length);
        Assert.Single(Outbox.Published);
    }

    [Fact]
    public async Task SettlementRunTwiceForTheSameEvent_IsANoOpOnTheFlags()
    {
        var saved = await SeedCheckoutAsync(CheckoutSeed.Lines(1, amount: 30m), OrderState.Executing);
        var merchant = saved.Order(saved.OrderId(0)).MerchantId;
        await ThroughTransactionBehaviorAsync(saved.CheckoutId, c => c.SucceedOrder(saved.OrderId(0)));
        Outbox.Reset();

        await using (var scope = NewScope())
            await scope.ServiceProvider.GetRequiredService<IPublisher>()
                .Publish(new PaymentOrderSucceededDomainEvent(saved.OrderId(0), saved.CheckoutId));

        Assert.Equal(30m, (await ReadWalletAsync(merchant))!.Balance);
        Assert.Equal(2, (await ReadLedgerAsync(saved.OrderId(0))).Length);
        Assert.Empty(Outbox.Published);
    }

    [Fact]
    public async Task FailingAnOrder_PublishesBookingPaymentFailedOnce_InsideTheTransaction_AndMovesNoMoney()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);

        await ThroughTransactionBehaviorAsync(saved.CheckoutId, c => c.FailOrder(saved.OrderId(0)));

        var published = Assert.Single(Outbox.Published);
        Assert.Equal(new BookingPaymentFailedIntegrationEvent(saved.BookingId), published.Event);
        Assert.Equal(LastTransactionId, published.TransactionId);
        Assert.Null(await ReadWalletAsync(saved.Order(saved.OrderId(0)).MerchantId));
        Assert.Empty(await ReadLedgerAsync(saved.OrderId(0)));
    }

    [Fact]
    public async Task RedeliveredFailure_PublishesNothingMore()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);
        await ThroughTransactionBehaviorAsync(saved.CheckoutId, c => c.FailOrder(saved.OrderId(0)));

        await ThroughTransactionBehaviorAsync(saved.CheckoutId, c => c.FailOrder(saved.OrderId(0)));

        Assert.Single(Outbox.Published);
    }

    [Fact]
    public async Task SettlementThrows_RollsBackTheSuccessTheWalletAndTheLedger()
    {
        // A wallet one cent short of the largest storable balance cannot take the credit, so settlement throws
        // after the order's success was already written inside the transaction.
        var merchant = Guid.NewGuid();
        var full = Wallet.Create(merchant, "USD");
        full.Credit(9999999999999999.98m, "USD");
        await InScopeAsync(async c => { c.Wallets.Add(full); await c.SaveChangesAsync(); });
        var saved = await SeedCheckoutAsync([new PaymentOrderLine(merchant, 1m, "USD")], OrderState.Executing);

        await Assert.ThrowsAsync<PaymentDomainException>(() =>
            ThroughTransactionBehaviorAsync(saved.CheckoutId, c => c.SucceedOrder(saved.OrderId(0))));

        var stored = await ReadCheckoutAsync(saved.CheckoutId);
        Assert.Equal(PaymentOrderStatus.Executing, stored.Order(saved.OrderId(0)).Status);
        Assert.False(stored.IsPaymentDone);
        Assert.False(stored.Order(saved.OrderId(0)).WalletUpdated);
        Assert.Equal(9999999999999999.98m, (await ReadWalletAsync(merchant))!.Balance);
        Assert.Empty(await ReadLedgerAsync(saved.OrderId(0)));
        Assert.Empty(Outbox.Published);
    }

    [Fact]
    public async Task FailureAfterBookingPaidWasStaged_RollsBackEverything_AndTheMessageWasInThatTransaction()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);
        var merchant = saved.Order(saved.OrderId(0)).MerchantId;
        // RecordingHandler runs after Settle for the same event, so this throws once BookingPaid is staged.
        Events.OnPublish = (_, _) => throw new InvalidOperationException("a later handler failed");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ThroughTransactionBehaviorAsync(saved.CheckoutId, c => c.SucceedOrder(saved.OrderId(0))));

        var stored = await ReadCheckoutAsync(saved.CheckoutId);
        Assert.Equal(PaymentOrderStatus.Executing, stored.Order(saved.OrderId(0)).Status);
        Assert.Null(await ReadWalletAsync(merchant));
        Assert.Empty(await ReadLedgerAsync(saved.OrderId(0)));
        // Staged inside the transaction that rolled back — the real outbox then never sends it, which
        // PaymentsHostTests proves against the broker.
        var staged = Assert.Single(Outbox.Published);
        Assert.IsType<BookingPaidIntegrationEvent>(staged.Event);
        Assert.Equal(LastTransactionId, staged.TransactionId);
    }
}
