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
        Assert.Equal(new RefundRequest(order.PaymentOrderId, CheckoutSeed.Token, order.Amount, order.Currency), refund);

        var stored = await ReadOrderAsync(order.PaymentOrderId);
        Assert.Equal(PaymentOrderStatus.Refunded, stored.Status);
        Assert.Equal(StubGateway.RefundReferenceFor(order.PaymentOrderId), stored.RefundReference);
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
        var command = new RecordRefund.Command(order.PaymentOrderId, "re_1");

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
        Assert.Equal("re_pending", stored.RefundReference);
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
