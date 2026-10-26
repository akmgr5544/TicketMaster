using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentProvider.Exceptions;
using PaymentProvider.Models;
using PaymentSystem.Enums;
using PaymentSystem.Features.PaymentOrders;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.PaymentOrders;

public sealed class StartCheckoutTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task The_buyer_opens_the_default_psps_page_and_the_order_starts_executing_there()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(amount: 64.25m, buyerId: buyer);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;

        var result = await Sender.Send(new StartCheckout.Command(buyer, orderId));

        Assert.True(result.IsSuccess);
        Assert.Equal("Stripe", result.Value!.Provider);
        Assert.False(string.IsNullOrEmpty(result.Value.ClientToken));

        Assert.True(Psp.Stripe.Checkouts.TryPeek(out var request));
        Assert.Equal(new CheckoutRequest(orderId, 64.25m, "USD"), request);

        var order = (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(orderId);
        Assert.Equal(PaymentOrderStatus.Executing, order.Status);
        Assert.Equal("Stripe", order.Provider);
        Assert.Equal(StubGateway.ReferenceFor(orderId), order.PspTokenRef);
    }

    [Fact]
    public async Task The_client_token_is_neither_stored_nor_logged()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(buyerId: buyer);

        var result = await Sender.Send(new StartCheckout.Command(buyer, checkout.PaymentOrders.Single().PaymentOrderId));
        var token = result.Value!.ClientToken;

        var pattern = $"%{token}%";
        var inCheckouts = await ReadAsync(c => c.Database
            .SqlQueryRaw<int>("select count(*)::int as \"Value\" from \"PaymentEvents\" e where e::text like {0}", pattern)
            .SingleAsync());
        var inOrders = await ReadAsync(c => c.Database
            .SqlQueryRaw<int>("select count(*)::int as \"Value\" from \"PaymentOrders\" o where o::text like {0}", pattern)
            .SingleAsync());
        Assert.Equal(0, inCheckouts);
        Assert.Equal(0, inOrders);
        Assert.DoesNotContain(Logs.Lines, line => line.Contains(token));
    }

    [Fact]
    public async Task Someone_other_than_the_buyer_finds_no_order()
    {
        var checkout = await SeedCheckoutAsync();
        var order = checkout.PaymentOrders.Single();

        var result = await Sender.Send(new StartCheckout.Command(order.SellerId, order.PaymentOrderId));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Empty(Psp.Stripe.Checkouts);
    }

    [Theory]
    [InlineData(OrderState.Success)]
    [InlineData(OrderState.Failed)]
    public async Task A_settled_order_cannot_be_paid_again(OrderState state)
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(state, buyerId: buyer);

        var result = await Sender.Send(new StartCheckout.Command(buyer, checkout.PaymentOrders.Single().PaymentOrderId));

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("payment_order_settled", result.Error.Code);
        Assert.Empty(Psp.Stripe.Checkouts);
    }

    [Fact]
    public async Task A_refunded_order_cannot_be_paid_again()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(OrderState.Success, buyerId: buyer);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        await InScopeAsync(async c =>
        {
            (await c.PaymentEvents.SingleAsync(e => e.CheckoutId == checkout.CheckoutId)).MarkOrderRefunded(orderId);
            await c.SaveChangesAsync();
        });

        var result = await Sender.Send(new StartCheckout.Command(buyer, orderId));

        Assert.Equal("payment_order_settled", result.Error!.Code);
    }

    [Fact]
    public async Task Asking_again_goes_back_to_the_orders_own_provider_and_changes_nothing()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(OrderState.Executing, buyerId: buyer, provider: "Braintree");
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        Psp.DefaultKind = PaymentProviderKind.Stripe;

        var result = await Sender.Send(new StartCheckout.Command(buyer, orderId));

        Assert.True(result.IsSuccess);
        Assert.Equal("Braintree", result.Value!.Provider);
        Assert.Empty(Psp.Stripe.Checkouts);
        Assert.Single(Psp.Braintree.Checkouts);
        var order = (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(orderId);
        Assert.Equal(PaymentOrderStatus.Executing, order.Status);
        Assert.Equal("Braintree", order.Provider);
        Assert.Equal(StubGateway.ReferenceFor(orderId), order.PspTokenRef);
    }

    // Reconciliation decides which orders are stuck by this timestamp, so it must be the moment the buyer was sent.
    [Fact]
    public async Task Starting_records_when_the_order_began_executing()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(buyerId: buyer);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        Clock.Advance(TimeSpan.FromMinutes(7));

        await Sender.Send(new StartCheckout.Command(buyer, orderId));

        var order = (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(orderId);
        Assert.Equal(Clock.GetUtcNow().UtcDateTime, order.ExecutingSince);
    }

    [Fact]
    public async Task The_buyers_saved_method_is_recorded_on_the_checkout()
    {
        var buyer = Guid.NewGuid();
        var method = await SeedPaymentMethodAsync(buyer, pspName: "Stripe");
        var checkout = await SeedCheckoutAsync(buyerId: buyer);

        var result = await Sender.Send(new StartCheckout.Command(buyer, checkout.PaymentOrders.Single().PaymentOrderId, method.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(method.Id, (await ReadCheckoutAsync(checkout.BookingId)).PaymentMethodId);
    }

    [Fact]
    public async Task Someone_elses_saved_method_is_not_found_and_nothing_starts()
    {
        var buyer = Guid.NewGuid();
        var method = await SeedPaymentMethodAsync(Guid.NewGuid(), pspName: "Stripe");
        var checkout = await SeedCheckoutAsync(buyerId: buyer);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;

        var result = await Sender.Send(new StartCheckout.Command(buyer, orderId, method.Id));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.Equal("payment_method_not_found", result.Error.Code);
        Assert.Empty(Psp.Stripe.Checkouts);
        var stored = await ReadCheckoutAsync(checkout.BookingId);
        Assert.Equal(PaymentOrderStatus.Init, stored.FindOrder(orderId).Status);
        Assert.Null(stored.PaymentMethodId);
    }

    // A saved token belongs to the PSP that issued it; another PSP cannot charge it.
    [Fact]
    public async Task A_saved_method_held_by_another_psp_is_refused_and_nothing_starts()
    {
        var buyer = Guid.NewGuid();
        var method = await SeedPaymentMethodAsync(buyer, pspName: "Braintree");
        var checkout = await SeedCheckoutAsync(buyerId: buyer);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        Psp.DefaultKind = PaymentProviderKind.Stripe;

        var result = await Sender.Send(new StartCheckout.Command(buyer, orderId, method.Id));

        Assert.Equal(ErrorType.BadRequest, result.Error!.Type);
        Assert.Equal("payment_method_provider_mismatch", result.Error.Code);
        Assert.Empty(Psp.Stripe.Checkouts);
        Assert.Empty(Psp.Braintree.Checkouts);
        var stored = await ReadCheckoutAsync(checkout.BookingId);
        Assert.Equal(PaymentOrderStatus.Init, stored.FindOrder(orderId).Status);
        Assert.Null(stored.PaymentMethodId);
    }

    // The hand-off is the point: a method a checkout was started with is what the delete guard protects.
    [Fact]
    public async Task A_saved_method_a_checkout_was_started_with_cannot_then_be_removed()
    {
        var buyer = Guid.NewGuid();
        var method = await SeedPaymentMethodAsync(buyer, pspName: "Stripe");
        var checkout = await SeedCheckoutAsync(buyerId: buyer);
        await Sender.Send(new StartCheckout.Command(buyer, checkout.PaymentOrders.Single().PaymentOrderId, method.Id));

        var result = await Sender.Send(new PaymentSystem.Features.PaymentMethods.DeletePaymentMethod.Command(buyer, method.Id));

        Assert.Equal("payment_method_in_use", result.Error!.Code);
        Assert.True(await ReadAsync(c => c.PaymentMethods.AnyAsync(m => m.Id == method.Id)));
    }

    [Theory]
    [InlineData(PaymentProviderErrorKind.Transient, ErrorType.Conflict, "psp_unavailable")]
    [InlineData(PaymentProviderErrorKind.InvalidRequest, ErrorType.BadRequest, "psp_rejected_request")]
    public async Task A_psp_error_is_answered_and_leaves_the_order_as_it_was(
        PaymentProviderErrorKind kind, ErrorType expectedType, string expectedCode)
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(buyerId: buyer);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        Psp.Stripe.OnCheckout = _ => throw new PaymentProviderException(PaymentProviderKind.Stripe, kind, "psp error");

        var result = await Sender.Send(new StartCheckout.Command(buyer, orderId));

        Assert.Equal(expectedType, result.Error!.Type);
        Assert.Equal(expectedCode, result.Error.Code);
        var order = (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(orderId);
        Assert.Equal(PaymentOrderStatus.Init, order.Status);
        Assert.Null(order.Provider);
    }

    // The race window is the PSP call: the handler has loaded the checkout and has not saved yet. The stub's hook runs
    // inside that window, so another writer changes the checkout exactly there. Task.Run keeps the hook's own awaits off
    // xUnit's synchronisation context, which the blocking wait would otherwise deadlock.
    private void DuringPspCall(Func<int, Task> otherWriter)
    {
        var answer = Psp.Stripe.OnCheckout;
        var calls = 0;
        Psp.Stripe.OnCheckout = request =>
        {
            Task.Run(() => otherWriter(++calls)).GetAwaiter().GetResult();
            return answer(request);
        };
    }

    [Fact]
    public async Task A_start_that_loses_to_the_expiry_is_decided_again_and_answers_that_the_order_is_settled()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(buyerId: buyer);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        DuringPspCall(async call =>
        {
            if (call == 1)
                await InScopeAsync(async c =>
                {
                    (await c.PaymentEvents.SingleAsync(e => e.CheckoutId == checkout.CheckoutId)).Cancel();
                    await c.SaveChangesAsync();
                });
        });

        var result = await Sender.Send(new StartCheckout.Command(buyer, orderId));

        Assert.False(result.IsSuccess);
        Assert.Equal("payment_order_settled", result.Error!.Code);
        Assert.Equal(PaymentOrderStatus.Failed, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(orderId).Status);
    }

    [Fact]
    public async Task A_start_that_loses_to_the_buyers_own_second_start_succeeds_and_starts_the_order_once()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(buyerId: buyer);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        DuringPspCall(async call =>
        {
            if (call == 1)
                await InScopeAsync(async c =>
                {
                    (await c.PaymentEvents.SingleAsync(e => e.CheckoutId == checkout.CheckoutId))
                        .StartOrder(orderId, "Stripe", StubGateway.ReferenceFor(orderId), Clock.GetUtcNow().UtcDateTime);
                    await c.SaveChangesAsync();
                });
        });

        var result = await Sender.Send(new StartCheckout.Command(buyer, orderId));

        Assert.True(result.IsSuccess);
        Assert.Equal("Stripe", result.Value!.Provider);
        Assert.False(string.IsNullOrEmpty(result.Value.ClientToken));
        var stored = await ReadCheckoutAsync(checkout.BookingId);
        Assert.Equal(PaymentOrderStatus.Executing, stored.FindOrder(orderId).Status);
        // Started once: only the other writer's start moved the version; the handler's own was a no-op.
        Assert.Equal(checkout.Version + 1, stored.Version);
    }

    [Fact]
    public async Task A_start_that_keeps_losing_gives_up_after_three_attempts()
    {
        var buyer = Guid.NewGuid();
        var checkout = await SeedCheckoutAsync(buyerId: buyer);
        var orderId = checkout.PaymentOrders.Single().PaymentOrderId;
        // A writer that changes the checkout on every attempt without settling it.
        DuringPspCall(_ => InScopeAsync(c => c.Database.ExecuteSqlRawAsync(
            "update \"PaymentEvents\" set \"Version\" = \"Version\" + 1 where \"CheckoutId\" = {0}", checkout.CheckoutId)));

        var result = await Sender.Send(new StartCheckout.Command(buyer, orderId));

        Assert.False(result.IsSuccess);
        Assert.Equal("payment_order_changed", result.Error!.Code);
        Assert.Equal(3, Psp.Stripe.Checkouts.Count);
        Assert.Equal(PaymentOrderStatus.Init, (await ReadCheckoutAsync(checkout.BookingId)).FindOrder(orderId).Status);
    }
}
