using MediatR;
using Microsoft.Extensions.DependencyInjection;
using PaymentIntegration.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Domain;
using PaymentSystem.Enums;
using PaymentSystem.Features.Checkouts;
using TicketMaster.Common.IntegrationEvents;

namespace PaymentIntegration.Features.Checkouts;

public sealed class RefundCheckoutTests : MessagingTest
{
    private readonly StubPsp _psp;
    private readonly LogCapture _logs;

    public RefundCheckoutTests(PaymentsFixture fixture) : base(fixture)
    {
        _psp = fixture.Services.GetRequiredService<StubPsp>();
        _psp.Reset();
        _logs = fixture.Services.GetRequiredService<LogCapture>();
        _logs.Clear();
    }

    [Fact]
    public async Task Refunding_a_paid_booking_reverses_the_money_and_tells_Bookings()
    {
        var checkout = await SettledCheckoutAsync(orders: 1);
        var order = checkout.PaymentOrders.Single();

        var result = await SendAsync(new RefundCheckout.Command(checkout.BookingId));

        Assert.Equal(new RefundCheckout.Response(1, 0), result.Value);
        var refund = Assert.Single(_psp.Stripe.Refunds);
        // Bookings named no refund, so the order's own id names its part: stable across redeliveries.
        Assert.Equal(new RefundRequest(order.PaymentOrderId, CheckoutSeed.Token, order.Amount, order.Currency,
            order.PaymentOrderId), refund);

        var stored = await ReadOrderAsync(order.PaymentOrderId);
        Assert.Equal(PaymentOrderStatus.Refunded, stored.Status);
        Assert.Equal(StubGateway.RefundReferenceFor(order.PaymentOrderId, order.PaymentOrderId),
            Assert.Single(stored.Refunds).ProviderReference);
        Assert.Equal(0m, (await ReadWalletAsync(order.MerchantId))!.Balance);

        var ledger = await ReadLedgerAsync(order.PaymentOrderId);
        Assert.Equal(4, ledger.Length);
        Assert.Equal(0m, ledger.Sum(e => e.Type == EntryType.Debit ? e.Amount : -e.Amount));
        var refundDebit = Assert.Single(ledger, e => e is { Reason: EntryReason.Refund, Type: EntryType.Debit });
        Assert.Equal(order.MerchantId, refundDebit.AccountId);

        var published = Assert.Single(Outbox.Published);
        Assert.Equal(new BookingRefundedIntegrationEvent(checkout.BookingId), published.Event);
        Assert.NotNull(published.TransactionId);
    }

    // At-least-once delivery: the second run finds nothing left to refund, so the provider is not asked again and
    // the money is not reversed twice.
    [Fact]
    public async Task A_redelivered_refund_changes_nothing_the_second_time()
    {
        var checkout = await SettledCheckoutAsync(orders: 1);
        var order = checkout.PaymentOrders.Single();
        await SendAsync(new RefundCheckout.Command(checkout.BookingId));

        var again = await SendAsync(new RefundCheckout.Command(checkout.BookingId));

        Assert.Equal(new RefundCheckout.Response(0, 0), again.Value);
        Assert.Single(_psp.Stripe.Refunds);
        Assert.Equal(4, (await ReadLedgerAsync(order.PaymentOrderId)).Length);
        Assert.Equal(0m, (await ReadWalletAsync(order.MerchantId))!.Balance);
        Assert.Single(Outbox.OfType<BookingRefundedIntegrationEvent>());
    }

    // The provider answers a repeated refund with the first one, so recording it twice must be a no-op too.
    [Fact]
    public async Task Recording_the_same_refund_twice_reverses_the_money_once()
    {
        var checkout = await SettledCheckoutAsync(orders: 1);
        var order = checkout.PaymentOrders.Single();
        var command = new RecordRefund.Command(order.PaymentOrderId, Guid.NewGuid(), order.Amount, "re_1",
            BookingRefundId: null, WholeCheckout: true);

        var first = await SendAsync(command);
        var second = await SendAsync(command);

        Assert.Equal(new RecordRefund.Response(true, true), first.Value);
        Assert.Equal(new RecordRefund.Response(false, false), second.Value);
        Assert.Equal(0m, (await ReadWalletAsync(order.MerchantId))!.Balance);
        Assert.Single(Outbox.OfType<BookingRefundedIntegrationEvent>());
    }

    [Fact]
    public async Task Every_paid_order_is_refunded_and_the_booking_is_reported_refunded_once()
    {
        var checkout = await SettledCheckoutAsync(orders: 2);

        var result = await SendAsync(new RefundCheckout.Command(checkout.BookingId));

        Assert.Equal(new RefundCheckout.Response(2, 0), result.Value);
        Assert.Equal(2, _psp.Stripe.Refunds.Count);
        Assert.All(checkout.PaymentOrders, order =>
            Assert.Equal(PaymentOrderStatus.Refunded, ReadOrderAsync(order.PaymentOrderId).Result.Status));
        Assert.Single(Outbox.OfType<BookingRefundedIntegrationEvent>());
    }

    // Only money that was taken goes back: a failed order is left alone and does not hold the booking up.
    [Fact]
    public async Task Only_the_paid_orders_of_a_partly_paid_checkout_are_refunded()
    {
        var checkout = await SeedCheckoutAsync(OrderState.Executing, OrderState.Failed);
        await ThroughTransactionBehaviorAsync(checkout.CheckoutId, c => c.SucceedOrder(checkout.OrderId(0)));
        Outbox.Reset();

        await SendAsync(new RefundCheckout.Command(checkout.BookingId));

        Assert.Equal(checkout.OrderId(0), Assert.Single(_psp.Stripe.Refunds).PaymentOrderId);
        Assert.Equal(PaymentOrderStatus.Failed, (await ReadOrderAsync(checkout.OrderId(1))).Status);
        Assert.Single(Outbox.OfType<BookingRefundedIntegrationEvent>());
    }

    // The provider has taken the instruction and holds the money for the buyer; it is recorded as given back.
    [Fact]
    public async Task A_pending_refund_is_recorded_as_refunded()
    {
        var checkout = await SettledCheckoutAsync(orders: 1);
        _psp.Stripe.OnRefund = request => new RefundResult("re_pending", RefundStatus.Pending);

        await SendAsync(new RefundCheckout.Command(checkout.BookingId));

        var stored = await ReadOrderAsync(checkout.OrderId(0));
        Assert.Equal(PaymentOrderStatus.Refunded, stored.Status);
        Assert.Equal("re_pending", Assert.Single(stored.Refunds).ProviderReference);
    }

    public static TheoryData<Func<RefundRequest, RefundResult>> Refusals => new()
    {
        _ => new RefundResult("re_failed", RefundStatus.Failed, "card closed"),
        _ => throw new PaymentProviderException(PaymentProviderKind.Stripe, PaymentProviderErrorKind.InvalidRequest, "no")
    };

    // Retrying cannot change a refusal, so it is left for a person: logged, nothing recorded, the booking not
    // reported refunded.
    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_refund_the_provider_refuses_leaves_the_payment_and_is_logged_for_a_person(
        Func<RefundRequest, RefundResult> provider)
    {
        var checkout = await SettledCheckoutAsync(orders: 1);
        var order = checkout.PaymentOrders.Single();
        _psp.Stripe.OnRefund = provider;

        var result = await SendAsync(new RefundCheckout.Command(checkout.BookingId));

        Assert.Equal(new RefundCheckout.Response(0, 1), result.Value);
        Assert.Equal(PaymentOrderStatus.Success, (await ReadOrderAsync(order.PaymentOrderId)).Status);
        Assert.Equal(order.Amount, (await ReadWalletAsync(order.MerchantId))!.Balance);
        Assert.Empty(Outbox.Published);
        Assert.Contains(_logs.Lines, line => line.StartsWith("Error") && line.Contains("manual refund"));
    }

    // Left to propagate, so the message's retry policy asks again once the provider is back.
    [Fact]
    public async Task A_provider_that_is_down_fails_the_message_and_records_nothing()
    {
        var checkout = await SettledCheckoutAsync(orders: 1);
        _psp.Stripe.OnRefund = _ =>
            throw new PaymentProviderException(PaymentProviderKind.Stripe, PaymentProviderErrorKind.Transient, "down");

        await Assert.ThrowsAsync<PaymentProviderException>(() => SendAsync(new RefundCheckout.Command(checkout.BookingId)));

        Assert.Equal(PaymentOrderStatus.Success, (await ReadOrderAsync(checkout.OrderId(0))).Status);
        Assert.Empty(Outbox.Published);
    }

    [Fact]
    public async Task A_booking_with_no_checkout_has_nothing_to_refund()
    {
        var result = await SendAsync(new RefundCheckout.Command(Random.Shared.NextInt64(1, long.MaxValue)));

        Assert.Equal(new RefundCheckout.Response(0, 0), result.Value);
        Assert.Empty(_psp.Stripe.Refunds);
    }

    [Fact]
    public async Task A_refund_requested_by_Bookings_refunds_the_booking()
    {
        var checkout = await SettledCheckoutAsync(orders: 1);

        await using (var scope = NewScope())
            await new RefundRequestedConsumer(scope.ServiceProvider.GetRequiredService<ISender>())
                .Consume(new RefundRequestedIntegrationEvent(checkout.BookingId), CancellationToken.None);

        Assert.Equal(PaymentOrderStatus.Refunded, (await ReadOrderAsync(checkout.OrderId(0))).Status);
    }

    [Fact]
    public async Task A_refund_due_after_a_cancellation_refunds_the_booking()
    {
        var checkout = await SettledCheckoutAsync(orders: 1);

        await using (var scope = NewScope())
            await new CheckoutRefundDueConsumer(scope.ServiceProvider.GetRequiredService<ISender>())
                .Consume(new CheckoutRefundDue(checkout.BookingId), CancellationToken.None);

        Assert.Equal(PaymentOrderStatus.Refunded, (await ReadOrderAsync(checkout.OrderId(0))).Status);
    }

    // --- Partial refunds: the seats a customer cancelled ---

    [Fact]
    public async Task A_partial_refund_gives_back_its_amount_and_reports_its_id()
    {
        var checkout = await SettledCheckoutAsync(orders: 1);
        var order = checkout.PaymentOrders.Single();
        var refundId = Guid.NewGuid();

        var result = await SendAsync(new RefundCheckout.Command(checkout.BookingId, refundId, 10m, "USD"));

        Assert.Equal(new RefundCheckout.Response(1, 0), result.Value);
        Assert.Equal(new RefundRequest(order.PaymentOrderId, CheckoutSeed.Token, 10m, "USD", refundId),
            Assert.Single(_psp.Stripe.Refunds));

        var stored = await ReadOrderAsync(order.PaymentOrderId);
        Assert.Equal(PaymentOrderStatus.Success, stored.Status);
        Assert.Equal(10m, stored.RefundedAmount);
        Assert.Equal(order.Amount - 10m, (await ReadWalletAsync(order.MerchantId))!.Balance);

        var refundPair = (await ReadLedgerAsync(order.PaymentOrderId)).Where(e => e.Reason == EntryReason.Refund).ToArray();
        Assert.Equal(2, refundPair.Length);
        Assert.All(refundPair, e => Assert.Equal((10m, (Guid?)refundId), (e.Amount, e.RefundId)));

        var published = Assert.Single(Outbox.Published);
        Assert.Equal(new BookingRefundedIntegrationEvent(checkout.BookingId, refundId), published.Event);
    }

    [Fact]
    public async Task Parts_that_add_up_to_the_order_refund_it_and_each_is_reported()
    {
        var checkout = await SettledCheckoutAsync(orders: 1);
        var (first, second) = (Guid.NewGuid(), Guid.NewGuid());

        await SendAsync(new RefundCheckout.Command(checkout.BookingId, first, 10m, "USD"));
        await SendAsync(new RefundCheckout.Command(checkout.BookingId, second, 15.50m, "USD"));

        var stored = await ReadOrderAsync(checkout.OrderId(0));
        Assert.Equal(PaymentOrderStatus.Refunded, stored.Status);
        Assert.Equal(6, (await ReadLedgerAsync(checkout.OrderId(0))).Length);
        Assert.Equal(0m, (await ReadWalletAsync(stored.MerchantId))!.Balance);
        Assert.Equal([first, second], Outbox.OfType<BookingRefundedIntegrationEvent>().Select(e => e.RefundId!.Value));
    }

    [Fact]
    public async Task A_redelivered_partial_refund_asks_the_provider_once()
    {
        var checkout = await SettledCheckoutAsync(orders: 1);
        var command = new RefundCheckout.Command(checkout.BookingId, Guid.NewGuid(), 10m, "USD");
        await SendAsync(command);

        var again = await SendAsync(command);

        Assert.Equal(new RefundCheckout.Response(0, 0), again.Value);
        Assert.Single(_psp.Stripe.Refunds);
        Assert.Equal(10m, (await ReadOrderAsync(checkout.OrderId(0))).RefundedAmount);
        Assert.Single(Outbox.OfType<BookingRefundedIntegrationEvent>());
    }

    // The relocation path asks for everything; after a partial refund that is only what is left.
    [Fact]
    public async Task A_whole_refund_after_a_partial_one_gives_back_the_rest()
    {
        var checkout = await SettledCheckoutAsync(orders: 1);
        var order = checkout.PaymentOrders.Single();
        await SendAsync(new RefundCheckout.Command(checkout.BookingId, Guid.NewGuid(), 10m, "USD"));
        var whole = Guid.NewGuid();

        await SendAsync(new RefundCheckout.Command(checkout.BookingId, whole));

        Assert.Equal(order.Amount - 10m, _psp.Stripe.Refunds.Last().Amount);
        Assert.Equal(PaymentOrderStatus.Refunded, (await ReadOrderAsync(order.PaymentOrderId)).Status);
        Assert.Equal(whole, Outbox.OfType<BookingRefundedIntegrationEvent>().Last().RefundId);
    }

    // A whole-booking refund got there first and gave everything back, this part's money with it.
    [Fact]
    public async Task A_partial_refund_after_a_whole_one_finds_nothing_left_and_asks_nobody()
    {
        var checkout = await SettledCheckoutAsync(orders: 1);
        await SendAsync(new RefundCheckout.Command(checkout.BookingId));

        var result = await SendAsync(new RefundCheckout.Command(checkout.BookingId, Guid.NewGuid(), 10m, "USD"));

        Assert.Equal(new RefundCheckout.Response(0, 0), result.Value);
        Assert.Single(_psp.Stripe.Refunds);
    }

    public static TheoryData<int, decimal, string> MisfitParts => new()
    {
        { 1, 25.51m, "USD" },   // more than was paid
        { 1, 10m, "EUR" },      // in another currency
        { 2, 10m, "USD" },      // two sellers: which order would it come off?
    };

    // Not a provider fault, so retrying will not fix it: logged for a person, the provider never asked.
    [Theory]
    [MemberData(nameof(MisfitParts))]
    public async Task A_partial_refund_that_does_not_fit_the_paid_orders_needs_a_person(int orders, decimal amount,
        string currency)
    {
        var checkout = await SettledCheckoutAsync(orders);

        var result = await SendAsync(new RefundCheckout.Command(checkout.BookingId, Guid.NewGuid(), amount, currency));

        Assert.Equal(new RefundCheckout.Response(0, 1), result.Value);
        Assert.Empty(_psp.Stripe.Refunds);
        Assert.Empty(Outbox.Published);
        Assert.Contains(_logs.Lines, line => line.StartsWith("Error") && line.Contains("manual refund"));
    }

    [Fact]
    public async Task A_partial_refund_requested_by_Bookings_refunds_that_much()
    {
        var checkout = await SettledCheckoutAsync(orders: 1);

        await using (var scope = NewScope())
            await new RefundRequestedConsumer(scope.ServiceProvider.GetRequiredService<ISender>())
                .Consume(new RefundRequestedIntegrationEvent(checkout.BookingId, Guid.NewGuid(), 10m, "USD"),
                    CancellationToken.None);

        Assert.Equal(10m, (await ReadOrderAsync(checkout.OrderId(0))).RefundedAmount);
    }

    // Settled by the real Settle handler, so there is a wallet credit and a pay-in pair for the refund to reverse.
    private async Task<PaymentEvent> SettledCheckoutAsync(int orders)
    {
        var checkout = await SeedCheckoutAsync(Enumerable.Repeat(OrderState.Executing, orders).ToArray());
        foreach (var order in checkout.PaymentOrders)
            await ThroughTransactionBehaviorAsync(checkout.CheckoutId, c => c.SucceedOrder(order.PaymentOrderId));
        Outbox.Reset();
        return checkout;
    }
}
