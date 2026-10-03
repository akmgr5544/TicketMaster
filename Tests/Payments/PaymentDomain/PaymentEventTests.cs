using PaymentSystem.Domain;
using PaymentSystem.Domain.Events;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Enums;
using static PaymentDomain.Checkouts;

namespace PaymentDomain;

public class PaymentEventTests
{
    private static readonly PaymentOrderStatus[] AllStatuses =
        [PaymentOrderStatus.NotStarted, PaymentOrderStatus.Executing, PaymentOrderStatus.Success, PaymentOrderStatus.Failed];

    private static PaymentEvent ThreeSellers() => WithLines(Line(10m), Line(20m), Line(30m, "EUR"));

    // --- Creation ---

    [Fact]
    public void A_new_checkout_is_not_paid()
    {
        var checkoutId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();

        var paymentEvent = NewCheckout(checkoutId, buyerId).WithOrders(Line());

        Assert.Equal(checkoutId, paymentEvent.CheckoutId);
        Assert.Equal(buyerId, paymentEvent.BuyerId);
        Assert.False(paymentEvent.IsPaymentDone);
        Assert.Empty(paymentEvent.DomainEvents);
    }

    [Fact]
    public void Refuses_an_empty_checkout_or_buyer()
    {
        Assert.Throws<PaymentDomainException>(() => PaymentEvent.Create(Guid.Empty, Random.Shared.NextInt64(1, long.MaxValue), Guid.NewGuid()));
        Assert.Throws<PaymentDomainException>(() => PaymentEvent.Create(Guid.NewGuid(), Random.Shared.NextInt64(1, long.MaxValue), Guid.Empty));
        Assert.Throws<PaymentDomainException>(() => PaymentEvent.Create(Guid.Empty, Random.Shared.NextInt64(1, long.MaxValue), Guid.Empty));
    }

    [Fact]
    public void Refuses_a_checkout_for_no_booking() =>
        Assert.Throws<PaymentDomainException>(() => PaymentEvent.Create(Guid.NewGuid(), 0, Guid.NewGuid()));

    // TrueForAll holds over an empty list; a checkout with nothing in it must never read as paid.
    [Fact]
    public void A_checkout_with_no_orders_yet_is_not_paid()
    {
        var paymentEvent = NewCheckout();

        Assert.Empty(paymentEvent.PaymentOrders);
        Assert.Equal(0, paymentEvent.OrderCount);
        Assert.False(paymentEvent.IsPaymentDone);
    }

    [Fact]
    public void Adding_an_order_counts_it()
    {
        var paymentEvent = NewCheckout().WithOrders(Line(), Line());

        Assert.Equal(2, paymentEvent.OrderCount);
    }

    [Theory]
    [InlineData(PaymentOrderStatus.Executing)]
    [InlineData(PaymentOrderStatus.Success)]
    [InlineData(PaymentOrderStatus.Failed)]
    public void Refuses_a_new_order_once_payment_has_begun(PaymentOrderStatus status)
    {
        var paymentEvent = SingleOrderIn(status);
        var before = Capture(paymentEvent);

        var thrown = Record.Exception(() => paymentEvent.WithOrders(Line()));

        Assert.IsType<PaymentDomainException>(thrown);
        Assert.Equal(before, Capture(paymentEvent));
    }

    public static TheoryData<OrderLine> BadLines => new()
    {
        new OrderLine(Guid.Empty, 10m, "USD"),
        new OrderLine(Guid.NewGuid(), 0m, "USD"),
        new OrderLine(Guid.NewGuid(), -1m, "USD"),
        new OrderLine(Guid.NewGuid(), 0.001m, "USD"),
        new OrderLine(Guid.NewGuid(), 10_000_000_000_000_000m, "USD"),
        new OrderLine(Guid.NewGuid(), 10m, "usd"),
        new OrderLine(Guid.NewGuid(), 10m, null!),
    };

    // A refused order must leave nothing behind, or the checkout would count an order it never got.
    [Theory]
    [MemberData(nameof(BadLines))]
    public void A_bad_order_is_refused_and_leaves_the_checkout_as_it_was(OrderLine bad)
    {
        var paymentEvent = WithLines(Line());
        var before = Capture(paymentEvent);

        var thrown = Record.Exception(() => paymentEvent.WithOrders(bad));

        Assert.IsType<PaymentDomainException>(thrown);
        Assert.Equal(before, Capture(paymentEvent));
        Assert.Equal(1, paymentEvent.OrderCount);
    }

    [Fact]
    public void Creates_one_order_per_line_carrying_the_checkout_and_buyer()
    {
        var checkoutId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        OrderLine[] lines = [Line(10m), Line(20.25m, "EUR"), Line(0.01m, "GBP")];

        var paymentEvent = NewCheckout(checkoutId, buyerId).WithOrders(lines);

        Assert.Equal(lines.Length, paymentEvent.PaymentOrders.Count);
        foreach (var line in lines)
        {
            var order = Assert.Single(paymentEvent.PaymentOrders, order => order.MerchantId == line.MerchantId);
            Assert.Equal(checkoutId, order.CheckoutId);
            Assert.Equal(buyerId, order.BuyerId);
            Assert.Equal(line.Amount, order.Amount);
            Assert.Equal(line.Currency, order.Currency);
            Assert.Equal(PaymentOrderStatus.NotStarted, order.Status);
        }
    }

    [Fact]
    public void Order_ids_are_unique_within_a_checkout()
    {
        var paymentEvent = WithLines(Enumerable.Range(0, 200).Select(_ => Line()).ToArray());

        var ids = paymentEvent.Ids();

        Assert.DoesNotContain(Guid.Empty, ids);
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    // The article's model is one payment order per seller; two orders to one seller double the PSP calls
    // and make "which order is this callback for" ambiguous for that seller.
    [Fact]
    public void Refuses_two_lines_for_the_same_seller()
    {
        var merchantId = Guid.NewGuid();

        var thrown = Record.Exception(() => WithLines(new OrderLine(merchantId, 10m, "USD"), new OrderLine(merchantId, 5m, "USD")));

        Assert.IsType<PaymentDomainException>(thrown);
    }

    [Fact]
    public void Refuses_the_same_seller_twice_among_other_sellers()
    {
        var merchantId = Guid.NewGuid();

        var thrown = Record.Exception(() => WithLines(Line(), new OrderLine(merchantId, 1m, "USD"), Line(), new OrderLine(merchantId, 2m, "EUR")));

        Assert.IsType<PaymentDomainException>(thrown);
    }

    [Fact]
    public void Refuses_a_buyer_who_is_one_of_several_sellers()
    {
        var buyerId = Guid.NewGuid();

        var thrown = Record.Exception(() => NewCheckout(buyerId: buyerId).WithOrders(Line(), new OrderLine(buyerId, 1m, "USD")));

        Assert.IsType<PaymentDomainException>(thrown);
    }

    [Fact]
    public void Two_checkouts_with_the_same_caller_key_keep_it()
    {
        var checkoutId = Guid.NewGuid();

        var first = NewCheckout(checkoutId).WithOrders(Line());
        var second = NewCheckout(checkoutId).WithOrders(Line());

        Assert.Equal(checkoutId, first.CheckoutId);
        Assert.Equal(checkoutId, second.CheckoutId);
        Assert.Empty(first.Ids().Intersect(second.Ids()));
    }

    // --- The orders collection is the root's, not the caller's ---

    [Fact]
    public void The_orders_cannot_be_added_to_or_removed_through_a_cast()
    {
        var paymentEvent = ThreeSellers();
        var stranger = SingleOrder().Only();

        if (paymentEvent.PaymentOrders is ICollection<PaymentOrder> collection)
        {
            Assert.ThrowsAny<NotSupportedException>(() => collection.Add(stranger));
            Assert.ThrowsAny<NotSupportedException>(() => collection.Remove(collection.First()));
            Assert.ThrowsAny<NotSupportedException>(() => collection.Clear());
        }

        Assert.Null(paymentEvent.PaymentOrders as List<PaymentOrder>);
        Assert.Null(paymentEvent.PaymentOrders as PaymentOrder[]);
        Assert.Equal(3, paymentEvent.PaymentOrders.Count);
        Assert.DoesNotContain(stranger, paymentEvent.PaymentOrders);
    }

    [Fact]
    public void The_orders_cannot_be_replaced_through_an_indexer()
    {
        var paymentEvent = ThreeSellers();
        var stranger = SingleOrder().Only();

        if (paymentEvent.PaymentOrders is IList<PaymentOrder> list)
            Assert.ThrowsAny<NotSupportedException>(() => list[0] = stranger);

        Assert.DoesNotContain(stranger, paymentEvent.PaymentOrders);
    }

    // --- Addressing an order the checkout does not own ---

    public static TheoryData<string> Operations => new() { "start", "succeed", "fail", "wallet", "ledger" };

    private static void Apply(PaymentEvent paymentEvent, Guid id, string operation)
    {
        switch (operation)
        {
            case "start": paymentEvent.StartExecuting(id, Provider, Token); break;
            case "succeed": paymentEvent.SucceedOrder(id); break;
            case "fail": paymentEvent.FailOrder(id); break;
            case "wallet": paymentEvent.MarkWalletUpdated(id); break;
            case "ledger": paymentEvent.MarkLedgerUpdated(id); break;
        }
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void An_order_from_another_checkout_is_refused_and_changes_neither_checkout(string operation)
    {
        var mine = ThreeSellers();
        var theirs = SingleOrder();
        // Put their order in a state where the operation would be legal, so only ownership can refuse it.
        theirs.Drive(theirs.OnlyId(), operation switch
        {
            "start" => PaymentOrderStatus.NotStarted,
            "succeed" or "fail" => PaymentOrderStatus.Executing,
            _ => PaymentOrderStatus.Success
        });
        var mineBefore = Capture(mine);
        var theirsBefore = Capture(theirs);

        var thrown = Record.Exception(() => Apply(mine, theirs.OnlyId(), operation));

        Assert.IsType<PaymentDomainException>(thrown);
        Assert.Equal(mineBefore, Capture(mine));
        Assert.Equal(theirsBefore, Capture(theirs));
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public void An_empty_or_unknown_order_id_is_refused_and_changes_nothing(string operation)
    {
        foreach (var id in new[] { Guid.Empty, Guid.NewGuid(), Guid.CreateVersion7() })
        {
            var paymentEvent = ThreeSellers();
            var before = Capture(paymentEvent);

            var thrown = Record.Exception(() => Apply(paymentEvent, id, operation));

            Assert.IsType<PaymentDomainException>(thrown);
            Assert.Equal(before, Capture(paymentEvent));
        }
    }

    // The checkout id is not an order id, even though both are Guids passed around the same handler.
    [Theory]
    [MemberData(nameof(Operations))]
    public void The_checkout_id_is_not_accepted_as_an_order_id(string operation)
    {
        var paymentEvent = ThreeSellers();
        var before = Capture(paymentEvent);

        Assert.Throws<PaymentDomainException>(() => Apply(paymentEvent, paymentEvent.CheckoutId, operation));
        Assert.Equal(before, Capture(paymentEvent));
    }

    [Fact]
    public void Operating_on_one_order_leaves_the_others_untouched()
    {
        var paymentEvent = ThreeSellers();
        var ids = paymentEvent.Ids();

        paymentEvent.Drive(ids[1], PaymentOrderStatus.Success);
        paymentEvent.MarkWalletUpdated(ids[1]);

        foreach (var other in new[] { ids[0], ids[2] })
        {
            var order = paymentEvent.Order(other);
            Assert.Equal(PaymentOrderStatus.NotStarted, order.Status);
            Assert.Null(order.PspToken);
            Assert.False(order.WalletUpdated);
        }
    }

    // --- IsPaymentDone ---

    [Fact]
    public void A_single_order_checkout_is_done_when_its_order_succeeds()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Executing);
        Assert.False(paymentEvent.IsPaymentDone);

        paymentEvent.SucceedOrder(paymentEvent.OnlyId());

        Assert.True(paymentEvent.IsPaymentDone);
    }

    [Theory]
    [InlineData(PaymentOrderStatus.NotStarted)]
    [InlineData(PaymentOrderStatus.Executing)]
    [InlineData(PaymentOrderStatus.Failed)]
    public void A_single_order_checkout_is_not_done_while_its_order_has_not_succeeded(PaymentOrderStatus status) =>
        Assert.False(SingleOrderIn(status).IsPaymentDone);

    // Every combination of three order statuses: done exactly when all three succeeded.
    [Fact]
    public void A_checkout_is_done_exactly_when_every_order_succeeded()
    {
        foreach (var a in AllStatuses)
        foreach (var b in AllStatuses)
        foreach (var c in AllStatuses)
        {
            var paymentEvent = ThreeSellers();
            var ids = paymentEvent.Ids();
            paymentEvent.Drive(ids[0], a);
            paymentEvent.Drive(ids[1], b);
            paymentEvent.Drive(ids[2], c);

            var allSucceeded = a == PaymentOrderStatus.Success && b == PaymentOrderStatus.Success && c == PaymentOrderStatus.Success;
            Assert.True(allSucceeded == paymentEvent.IsPaymentDone, $"[{a}, {b}, {c}] gave IsPaymentDone={paymentEvent.IsPaymentDone}.");
        }
    }

    public static TheoryData<int[]> SettlementOrders => new()
    {
        new[] { 0, 1, 2 }, new[] { 0, 2, 1 }, new[] { 1, 0, 2 }, new[] { 1, 2, 0 }, new[] { 2, 0, 1 }, new[] { 2, 1, 0 }
    };

    [Theory]
    [MemberData(nameof(SettlementOrders))]
    public void The_order_in_which_sellers_settle_does_not_matter(int[] sequence)
    {
        var paymentEvent = ThreeSellers();
        var ids = paymentEvent.Ids();
        foreach (var id in ids)
            paymentEvent.StartExecuting(id, Provider, Token);

        for (var i = 0; i < sequence.Length; i++)
        {
            Assert.False(paymentEvent.IsPaymentDone, $"Done after only {i} of 3 orders succeeded.");
            paymentEvent.SucceedOrder(ids[sequence[i]]);
        }

        Assert.True(paymentEvent.IsPaymentDone);
    }

    [Fact]
    public void A_redelivered_success_does_not_complete_a_checkout_with_an_unsettled_order()
    {
        var paymentEvent = WithLines(Line(), Line());
        var ids = paymentEvent.Ids();
        paymentEvent.Drive(ids[0], PaymentOrderStatus.Success);
        paymentEvent.StartExecuting(ids[1], Provider, Token);

        paymentEvent.SucceedOrder(ids[0]);
        paymentEvent.SucceedOrder(ids[0]);

        Assert.False(paymentEvent.IsPaymentDone);
    }

    [Fact]
    public void A_done_checkout_stays_done_through_every_later_call()
    {
        var paymentEvent = WithLines(Line(), Line());
        var ids = paymentEvent.Ids();
        foreach (var id in ids)
            paymentEvent.Drive(id, PaymentOrderStatus.Success);
        Assert.True(paymentEvent.IsPaymentDone);

        foreach (var id in ids)
        {
            Record.Exception(() => paymentEvent.FailOrder(id));
            Record.Exception(() => paymentEvent.StartExecuting(id, Provider, "another"));
            paymentEvent.SucceedOrder(id);
            paymentEvent.MarkWalletUpdated(id);
            paymentEvent.MarkLedgerUpdated(id);
            Record.Exception(() => paymentEvent.SucceedOrder(Guid.NewGuid()));
        }

        Assert.True(paymentEvent.IsPaymentDone);
    }

    [Fact]
    public void Settling_raises_one_event_per_order_naming_that_order()
    {
        var paymentEvent = ThreeSellers();
        var ids = paymentEvent.Ids();

        paymentEvent.Drive(ids[0], PaymentOrderStatus.Success);
        paymentEvent.Drive(ids[1], PaymentOrderStatus.Failed);
        paymentEvent.Drive(ids[2], PaymentOrderStatus.Success);

        Assert.Equal(
            [
                new PaymentOrderSucceededDomainEvent(ids[0], paymentEvent.CheckoutId),
                new PaymentOrderFailedDomainEvent(ids[1], paymentEvent.CheckoutId),
                new PaymentOrderSucceededDomainEvent(ids[2], paymentEvent.CheckoutId)
            ],
            paymentEvent.DomainEvents.ToArray());
    }

    [Fact]
    public void Orders_and_domain_events_live_on_the_root_only()
    {
        Assert.False(typeof(PaymentSystem.Domain.Abstractions.Entity).IsAssignableFrom(typeof(PaymentOrder)));
        Assert.True(typeof(PaymentSystem.Domain.Abstractions.Entity).IsAssignableFrom(typeof(PaymentEvent)));
    }

    // --- Expiry and cancellation ---

    public static TheoryData<string> Abandonments => new() { "expire", "cancel" };

    private static int Abandon(PaymentEvent paymentEvent, string how) =>
        how == "expire" ? paymentEvent.Expire() : paymentEvent.Cancel();

    // Every combination of three order statuses: what was unpaid fails, what was settled stays as it was.
    [Theory]
    [MemberData(nameof(Abandonments))]
    public void Abandoning_fails_every_unsettled_order_and_leaves_settled_ones(string how)
    {
        foreach (var a in AllStatuses)
        foreach (var b in AllStatuses)
        foreach (var c in AllStatuses)
        {
            var paymentEvent = ThreeSellers();
            var ids = paymentEvent.Ids();
            PaymentOrderStatus[] states = [a, b, c];
            for (var i = 0; i < ids.Length; i++)
                paymentEvent.Drive(ids[i], states[i]);
            paymentEvent.ClearDomainEvents();
            var unsettled = ids.Where((_, i) => states[i] is PaymentOrderStatus.NotStarted or PaymentOrderStatus.Executing).ToArray();

            var failed = Abandon(paymentEvent, how);

            var label = $"[{a}, {b}, {c}]";
            Assert.True(unsettled.Length == failed, $"{label} failed {failed}.");
            for (var i = 0; i < ids.Length; i++)
            {
                var expected = unsettled.Contains(ids[i]) ? PaymentOrderStatus.Failed : states[i];
                Assert.True(expected == paymentEvent.Order(ids[i]).Status, $"{label} left order {i} {paymentEvent.Order(ids[i]).Status}.");
            }
            Assert.Equal(
                unsettled.Select(id => (object)new PaymentOrderFailedDomainEvent(id, paymentEvent.CheckoutId)),
                paymentEvent.DomainEvents.Cast<object>());
            Assert.True(states.All(s => s == PaymentOrderStatus.Success) == paymentEvent.IsPaymentDone, $"{label} done={paymentEvent.IsPaymentDone}.");
        }
    }

    [Theory]
    [MemberData(nameof(Abandonments))]
    public void Abandoning_again_is_a_no_op(string how)
    {
        var paymentEvent = ThreeSellers();
        var ids = paymentEvent.Ids();
        paymentEvent.Drive(ids[0], PaymentOrderStatus.Success);
        paymentEvent.Drive(ids[1], PaymentOrderStatus.Executing);
        Abandon(paymentEvent, how);
        var before = Capture(paymentEvent);

        Assert.Equal(0, paymentEvent.Expire());
        Assert.Equal(0, paymentEvent.Cancel());
        Assert.Equal(before, Capture(paymentEvent));
    }

    [Theory]
    [MemberData(nameof(Abandonments))]
    public void A_success_reported_after_abandoning_is_refused(string how)
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Executing);
        Abandon(paymentEvent, how);
        var before = Capture(paymentEvent);

        Assert.Throws<PaymentDomainException>(() => paymentEvent.SucceedOrder(paymentEvent.OnlyId()));
        Assert.Throws<PaymentDomainException>(() => paymentEvent.StartExecuting(paymentEvent.OnlyId(), Provider, Token));
        Assert.Equal(before, Capture(paymentEvent));
    }

    [Theory]
    [MemberData(nameof(Abandonments))]
    public void Abandoning_a_paid_checkout_changes_nothing(string how)
    {
        var paymentEvent = WithLines(Line(), Line());
        foreach (var id in paymentEvent.Ids())
            paymentEvent.Drive(id, PaymentOrderStatus.Success);
        var before = Capture(paymentEvent);

        Assert.Equal(0, Abandon(paymentEvent, how));
        Assert.Equal(before, Capture(paymentEvent));
        Assert.True(paymentEvent.IsPaymentDone);
    }

    // --- IsPaymentDone is derived, not remembered ---
    // Stands in for a stale copy: a root reloaded on its own, whose stored flag lags its orders.

    private static void ForgetDone(PaymentEvent paymentEvent, bool value) =>
        typeof(PaymentEvent).GetProperty(nameof(PaymentEvent.IsPaymentDone))!.SetValue(paymentEvent, value);

    public static TheoryData<string> NoOpsOnAPaidCheckout => new() { "succeed", "wallet", "ledger", "expire", "cancel" };

    [Theory]
    [MemberData(nameof(NoOpsOnAPaidCheckout))]
    public void A_no_op_on_a_paid_checkout_restores_a_lagging_done_flag(string operation)
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Success);
        var id = paymentEvent.OnlyId();
        paymentEvent.MarkWalletUpdated(id);
        paymentEvent.MarkLedgerUpdated(id);
        ForgetDone(paymentEvent, false);

        switch (operation)
        {
            case "succeed": paymentEvent.SucceedOrder(id); break;
            case "wallet": paymentEvent.MarkWalletUpdated(id); break;
            case "ledger": paymentEvent.MarkLedgerUpdated(id); break;
            case "expire": paymentEvent.Expire(); break;
            case "cancel": paymentEvent.Cancel(); break;
        }

        Assert.True(paymentEvent.IsPaymentDone);
    }

    [Fact]
    public void A_replayed_start_clears_a_done_flag_the_orders_do_not_support()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Executing);
        ForgetDone(paymentEvent, true);

        paymentEvent.StartExecuting(paymentEvent.OnlyId(), Provider, Token);

        Assert.False(paymentEvent.IsPaymentDone);
    }

    [Fact]
    public void A_refused_call_leaves_the_done_flag_as_it_was()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Success);
        ForgetDone(paymentEvent, false);

        Assert.Throws<PaymentDomainException>(() => paymentEvent.FailOrder(paymentEvent.OnlyId()));

        Assert.False(paymentEvent.IsPaymentDone);
    }

    // --- Version ---
    // A save-time concurrency token: AggregateVersionInterceptor bumps it from the version as loaded. A
    // domain-side bump broke the disconnected Update path, so the domain must never move it. The per-save
    // increment is covered in PaymentIntegration's VersionTests.

    [Fact]
    public void A_real_change_leaves_the_version_to_the_save()
    {
        var paymentEvent = WithLines(Line(), Line());
        var ids = paymentEvent.Ids();
        var steps = new Action[]
        {
            () => paymentEvent.StartExecuting(ids[0], Provider, Token),
            () => paymentEvent.SucceedOrder(ids[0]),
            () => paymentEvent.MarkWalletUpdated(ids[0]),
            () => paymentEvent.MarkLedgerUpdated(ids[0]),
            () => paymentEvent.StartExecuting(ids[1], Provider, Token),
            () => paymentEvent.FailOrder(ids[1]),
        };

        foreach (var step in steps)
        {
            var before = paymentEvent.Version;
            step();
            Assert.Equal(before, paymentEvent.Version);
        }
    }

    [Fact]
    public void A_redelivered_no_op_does_not_bump_the_version()
    {
        var paymentEvent = WithLines(Line(), Line());
        var ids = paymentEvent.Ids();
        paymentEvent.Drive(ids[0], PaymentOrderStatus.Success);
        paymentEvent.MarkWalletUpdated(ids[0]);
        paymentEvent.MarkLedgerUpdated(ids[0]);
        paymentEvent.Drive(ids[1], PaymentOrderStatus.Failed);
        var version = paymentEvent.Version;

        paymentEvent.SucceedOrder(ids[0]);
        paymentEvent.MarkWalletUpdated(ids[0]);
        paymentEvent.MarkLedgerUpdated(ids[0]);
        paymentEvent.FailOrder(ids[1]);

        Assert.Equal(version, paymentEvent.Version);
    }

    [Fact]
    public void A_refused_call_does_not_bump_the_version()
    {
        var paymentEvent = WithLines(Line(), Line());
        var ids = paymentEvent.Ids();
        paymentEvent.Drive(ids[0], PaymentOrderStatus.Success);
        paymentEvent.Drive(ids[1], PaymentOrderStatus.Failed);
        var version = paymentEvent.Version;
        var refused = new Action[]
        {
            () => paymentEvent.StartExecuting(ids[0], Provider, "another"),
            () => paymentEvent.StartExecuting(ids[1], Provider, " "),
            () => paymentEvent.StartExecuting(ids[1], Provider, new string('t', 201)),
            () => paymentEvent.FailOrder(ids[0]),
            () => paymentEvent.SucceedOrder(ids[1]),
            () => paymentEvent.MarkWalletUpdated(ids[1]),
            () => paymentEvent.MarkLedgerUpdated(ids[1]),
            () => paymentEvent.SucceedOrder(Guid.Empty),
            () => paymentEvent.StartExecuting(Guid.NewGuid(), Provider, Token),
        };

        foreach (var call in refused)
        {
            Assert.Throws<PaymentDomainException>(call);
            Assert.Equal(version, paymentEvent.Version);
        }
    }

    [Fact]
    public void A_new_checkout_starts_at_version_zero_and_stays_there_through_its_lifecycle()
    {
        var paymentEvent = ThreeSellers();

        foreach (var id in paymentEvent.Ids())
        {
            paymentEvent.Drive(id, PaymentOrderStatus.Success);
            paymentEvent.MarkWalletUpdated(id);
            paymentEvent.MarkLedgerUpdated(id);
        }

        Assert.Equal(0, paymentEvent.Version);
    }
}
