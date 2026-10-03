using System.Globalization;
using System.Reflection;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Abstractions;
using PaymentSystem.Domain.Events;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Enums;
using static PaymentDomain.Checkouts;

namespace PaymentDomain;

// A payment order is a child of its checkout, so every rule here is exercised through PaymentEvent.
public class PaymentOrderTests
{
    public enum Operation
    {
        StartExecuting,
        Succeed,
        Fail,
        MarkWalletUpdated,
        MarkLedgerUpdated,
        Expire,
        Cancel
    }

    public enum Outcome
    {
        Allowed,
        Refused,
        NoOp
    }

    private static void Apply(PaymentEvent paymentEvent, Guid id, Operation operation)
    {
        switch (operation)
        {
            // A different token than the one used to reach Executing, so a start from any later state is a
            // genuine second start, not a redelivery of the first.
            case Operation.StartExecuting: paymentEvent.StartExecuting(id, Provider, "other-token"); break;
            case Operation.Succeed: paymentEvent.SucceedOrder(id); break;
            case Operation.Fail: paymentEvent.FailOrder(id); break;
            case Operation.MarkWalletUpdated: paymentEvent.MarkWalletUpdated(id); break;
            case Operation.MarkLedgerUpdated: paymentEvent.MarkLedgerUpdated(id); break;
            case Operation.Expire: paymentEvent.Expire(); break;
            case Operation.Cancel: paymentEvent.Cancel(); break;
        }
    }

    public static TheoryData<PaymentOrderStatus, Operation, Outcome> Transitions => new()
    {
        { PaymentOrderStatus.NotStarted, Operation.StartExecuting, Outcome.Allowed },
        { PaymentOrderStatus.NotStarted, Operation.Succeed, Outcome.Refused },
        { PaymentOrderStatus.NotStarted, Operation.Fail, Outcome.Refused },
        { PaymentOrderStatus.NotStarted, Operation.MarkWalletUpdated, Outcome.Refused },
        { PaymentOrderStatus.NotStarted, Operation.MarkLedgerUpdated, Outcome.Refused },
        { PaymentOrderStatus.NotStarted, Operation.Expire, Outcome.Allowed },
        { PaymentOrderStatus.NotStarted, Operation.Cancel, Outcome.Allowed },

        { PaymentOrderStatus.Executing, Operation.StartExecuting, Outcome.Refused },
        { PaymentOrderStatus.Executing, Operation.Succeed, Outcome.Allowed },
        { PaymentOrderStatus.Executing, Operation.Fail, Outcome.Allowed },
        { PaymentOrderStatus.Executing, Operation.MarkWalletUpdated, Outcome.Refused },
        { PaymentOrderStatus.Executing, Operation.MarkLedgerUpdated, Outcome.Refused },
        { PaymentOrderStatus.Executing, Operation.Expire, Outcome.Allowed },
        { PaymentOrderStatus.Executing, Operation.Cancel, Outcome.Allowed },

        { PaymentOrderStatus.Success, Operation.StartExecuting, Outcome.Refused },
        { PaymentOrderStatus.Success, Operation.Succeed, Outcome.NoOp },
        { PaymentOrderStatus.Success, Operation.Fail, Outcome.Refused },
        { PaymentOrderStatus.Success, Operation.MarkWalletUpdated, Outcome.Allowed },
        { PaymentOrderStatus.Success, Operation.MarkLedgerUpdated, Outcome.Allowed },
        // A success already moved money; abandoning the checkout does not undo it.
        { PaymentOrderStatus.Success, Operation.Expire, Outcome.NoOp },
        { PaymentOrderStatus.Success, Operation.Cancel, Outcome.NoOp },

        { PaymentOrderStatus.Failed, Operation.StartExecuting, Outcome.Refused },
        { PaymentOrderStatus.Failed, Operation.Succeed, Outcome.Refused },
        { PaymentOrderStatus.Failed, Operation.Fail, Outcome.NoOp },
        { PaymentOrderStatus.Failed, Operation.MarkWalletUpdated, Outcome.Refused },
        { PaymentOrderStatus.Failed, Operation.MarkLedgerUpdated, Outcome.Refused },
        { PaymentOrderStatus.Failed, Operation.Expire, Outcome.NoOp },
        { PaymentOrderStatus.Failed, Operation.Cancel, Outcome.NoOp },
    };

    [Theory]
    [MemberData(nameof(Transitions))]
    public void Every_operation_from_every_state_is_allowed_refused_or_a_no_op(PaymentOrderStatus from, Operation operation, Outcome outcome)
    {
        var paymentEvent = SingleOrderIn(from);
        var id = paymentEvent.OnlyId();
        var before = Capture(paymentEvent);
        var versionBefore = paymentEvent.Version;
        var eventsBefore = paymentEvent.DomainEvents.Length;

        var thrown = Record.Exception(() => Apply(paymentEvent, id, operation));

        switch (outcome)
        {
            case Outcome.Refused:
                Assert.IsType<PaymentDomainException>(thrown);
                Assert.Equal(before, Capture(paymentEvent));
                break;
            case Outcome.NoOp:
                Assert.Null(thrown);
                Assert.Equal(before, Capture(paymentEvent));
                break;
            case Outcome.Allowed:
                Assert.Null(thrown);
                // The version is a save-time concurrency token (AggregateVersionInterceptor); the domain never moves it.
                Assert.Equal(versionBefore, paymentEvent.Version);
                AssertAllowedTransition(paymentEvent, id, operation, eventsBefore);
                break;
        }
    }

    private static void AssertAllowedTransition(PaymentEvent paymentEvent, Guid id, Operation operation, int eventsBefore)
    {
        var order = paymentEvent.Order(id);
        switch (operation)
        {
            case Operation.StartExecuting:
                Assert.Equal(PaymentOrderStatus.Executing, order.Status);
                Assert.Equal("other-token", order.PspToken);
                Assert.Equal(eventsBefore, paymentEvent.DomainEvents.Length);
                break;
            case Operation.Succeed:
                Assert.Equal(PaymentOrderStatus.Success, order.Status);
                Assert.Equal(new PaymentOrderSucceededDomainEvent(id, paymentEvent.CheckoutId), Assert.Single(paymentEvent.DomainEvents));
                break;
            case Operation.Fail:
            case Operation.Expire:
            case Operation.Cancel:
                Assert.Equal(PaymentOrderStatus.Failed, order.Status);
                Assert.Equal(new PaymentOrderFailedDomainEvent(id, paymentEvent.CheckoutId), Assert.Single(paymentEvent.DomainEvents));
                break;
            case Operation.MarkWalletUpdated:
                Assert.True(order.WalletUpdated);
                Assert.False(order.LedgerUpdated);
                Assert.Equal(eventsBefore, paymentEvent.DomainEvents.Length);
                break;
            case Operation.MarkLedgerUpdated:
                Assert.True(order.LedgerUpdated);
                Assert.False(order.WalletUpdated);
                Assert.Equal(eventsBefore, paymentEvent.DomainEvents.Length);
                break;
        }
    }

    // --- Creation, through the checkout's lines ---

    [Fact]
    public void A_new_order_has_not_started_and_carries_its_line()
    {
        var line = Line(25.50m, "USD");
        var checkoutId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();

        var order = NewCheckout(checkoutId, buyerId).WithOrders(line).Only();

        Assert.Equal(PaymentOrderStatus.NotStarted, order.Status);
        Assert.NotEqual(Guid.Empty, order.PaymentOrderId);
        Assert.Null(order.PspToken);
        Assert.False(order.WalletUpdated);
        Assert.False(order.LedgerUpdated);
        Assert.Equal(checkoutId, order.CheckoutId);
        Assert.Equal(buyerId, order.BuyerId);
        Assert.Equal(line.MerchantId, order.MerchantId);
        Assert.Equal(25.50m, order.Amount);
        Assert.Equal("USD", order.Currency);
    }

    [Fact]
    public void Refuses_an_empty_merchant()
    {
        var thrown = Record.Exception(() => WithLines(new OrderLine(Guid.Empty, 10m, "USD")));

        Assert.IsType<PaymentDomainException>(thrown);
    }

    // Debit and credit would land on the same account, so the pay-in moves nothing while still being recorded.
    [Fact]
    public void Refuses_a_buyer_paying_themselves()
    {
        var party = Guid.NewGuid();

        var thrown = Record.Exception(() => NewCheckout(buyerId: party).WithOrders(new OrderLine(party, 10m, "USD")));

        Assert.IsType<PaymentDomainException>(thrown);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-0.0")]
    [InlineData("-1")]
    [InlineData("-0.01")]
    [InlineData("-79228162514264337593543950335")]
    public void Refuses_a_non_positive_amount(string amount)
    {
        var thrown = Record.Exception(() => SingleOrder(decimal.Parse(amount, CultureInfo.InvariantCulture)));

        Assert.IsType<PaymentDomainException>(thrown);
    }

    [Theory]
    [InlineData("0.01")]
    [InlineData("10.10")]
    [InlineData("10.100")]
    [InlineData("9999999999999999.99")]
    public void Accepts_an_amount_numeric_18_2_holds_exactly(string amount)
    {
        var value = decimal.Parse(amount, CultureInfo.InvariantCulture);

        Assert.Equal(value, SingleOrder(value).Only().Amount);
    }

    // numeric(18,2) would round these on save, so what the PSP charges and what the ledger records diverge.
    [Theory]
    [InlineData("0.001")]
    [InlineData("0.005")]
    [InlineData("10.005")]
    [InlineData("25.999")]
    [InlineData("0.0000000000000000000000000001")]
    public void Refuses_an_amount_with_more_than_two_decimal_places(string amount)
    {
        var thrown = Record.Exception(() => SingleOrder(decimal.Parse(amount, CultureInfo.InvariantCulture)));

        Assert.IsType<PaymentDomainException>(thrown);
    }

    [Theory]
    [InlineData("10000000000000000")]
    [InlineData("10000000000000000.00")]
    [InlineData("79228162514264337593543950335")]
    public void Refuses_an_amount_too_large_for_numeric_18_2(string amount)
    {
        var thrown = Record.Exception(() => SingleOrder(decimal.Parse(amount, CultureInfo.InvariantCulture)));

        Assert.IsType<PaymentDomainException>(thrown);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("usd")]
    [InlineData("Usd")]
    [InlineData("uSD")]
    [InlineData("US")]
    [InlineData("USDT")]
    [InlineData(" USD")]
    [InlineData("USD ")]
    [InlineData("U D")]
    [InlineData("U$D")]
    [InlineData("US1")]
    [InlineData("123")]
    [InlineData("ÜSD")]
    [InlineData("ＵＳＤ")]
    [InlineData("USD\0")]
    public void Refuses_a_currency_that_is_not_an_iso_code(string? currency)
    {
        var thrown = Record.Exception(() => SingleOrder(10m, currency!));

        Assert.IsType<PaymentDomainException>(thrown);
    }

    [Fact]
    public void Every_order_gets_its_own_payment_order_id()
    {
        var ids = Enumerable.Range(0, 1_000).Select(_ => SingleOrder().OnlyId()).ToList();

        Assert.DoesNotContain(Guid.Empty, ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    // The id is the PSP idempotency key: if any transition re-minted it, a retry would charge twice.
    [Fact]
    public void The_payment_order_id_survives_every_transition()
    {
        var paymentEvent = SingleOrder();
        var id = paymentEvent.OnlyId();

        paymentEvent.StartExecuting(id, Provider, Token);
        Assert.Equal(id, paymentEvent.OnlyId());
        paymentEvent.SucceedOrder(id);
        Assert.Equal(id, paymentEvent.OnlyId());
        paymentEvent.MarkWalletUpdated(id);
        paymentEvent.MarkLedgerUpdated(id);
        Assert.Equal(id, paymentEvent.OnlyId());
    }

    // The order is reachable from the collection; only the root may move it.
    [Fact]
    public void A_payment_order_exposes_no_public_way_to_change_itself()
    {
        var publicMutators = typeof(PaymentOrder)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .ToList();
        var publicSetters = typeof(PaymentOrder)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.Name)
            .ToList();

        Assert.Empty(publicMutators);
        Assert.Empty(publicSetters);
    }

    // --- PSP token ---

    [Fact]
    public void Starting_records_the_psp_token()
    {
        var order = SingleOrderIn(PaymentOrderStatus.Executing).Only();

        Assert.Equal(PaymentOrderStatus.Executing, order.Status);
        Assert.Equal(Token, order.PspToken);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    public void Refuses_a_blank_psp_token_and_changes_nothing(string token)
    {
        var paymentEvent = SingleOrder();
        var before = Capture(paymentEvent);

        var thrown = Record.Exception(() => paymentEvent.StartExecuting(paymentEvent.OnlyId(), Provider, token));

        Assert.IsType<PaymentDomainException>(thrown);
        Assert.Equal(before, Capture(paymentEvent));
    }

    // Braintree creates nothing until the payment method is submitted, so there is no reference to record.
    [Fact]
    public void Starts_without_a_psp_token_for_a_provider_that_has_none_yet()
    {
        var paymentEvent = SingleOrder();

        paymentEvent.StartExecuting(paymentEvent.OnlyId(), Provider, null);

        Assert.Equal(PaymentOrderStatus.Executing, paymentEvent.Only().Status);
        Assert.Null(paymentEvent.Only().PspToken);
    }

    [Fact]
    public void Accepts_a_psp_token_of_exactly_200_characters()
    {
        var paymentEvent = SingleOrder();
        var token = new string('t', 200);

        paymentEvent.StartExecuting(paymentEvent.OnlyId(), Provider, token);

        Assert.Equal(token, paymentEvent.Only().PspToken);
    }

    // PspToken is varchar(200): a longer token would pass the domain and fail the save after the PSP session exists.
    [Fact]
    public void Refuses_a_psp_token_longer_than_the_column_and_changes_nothing()
    {
        var paymentEvent = SingleOrder();
        var before = Capture(paymentEvent);

        var thrown = Record.Exception(() => paymentEvent.StartExecuting(paymentEvent.OnlyId(), Provider, new string('t', 201)));

        Assert.IsType<PaymentDomainException>(thrown);
        Assert.Equal(before, Capture(paymentEvent));
    }

    [Fact]
    public void A_missing_token_on_an_executing_order_keeps_the_original_token()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Executing);
        var before = Capture(paymentEvent);

        Assert.Throws<PaymentDomainException>(() => paymentEvent.StartExecuting(paymentEvent.OnlyId(), Provider, " "));
        Assert.Equal(before, Capture(paymentEvent));
    }

    // At-least-once delivery: the same start message arriving twice should not blow up the consumer.
    [Fact]
    public void Starting_again_with_the_same_token_is_a_no_op()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Executing);
        var before = Capture(paymentEvent);

        var thrown = Record.Exception(() => paymentEvent.StartExecuting(paymentEvent.OnlyId(), Provider, Token));

        Assert.Null(thrown);
        Assert.Equal(before, Capture(paymentEvent));
    }

    // Braintree's start carries no token, so its replay is null after null.
    [Fact]
    public void Starting_again_without_a_token_after_starting_without_one_is_a_no_op()
    {
        var paymentEvent = SingleOrder();
        paymentEvent.StartExecuting(paymentEvent.OnlyId(), Provider, null);
        var before = Capture(paymentEvent);

        var thrown = Record.Exception(() => paymentEvent.StartExecuting(paymentEvent.OnlyId(), Provider, null));

        Assert.Null(thrown);
        Assert.Equal(before, Capture(paymentEvent));
    }

    [Theory]
    [InlineData(null, Token)]
    [InlineData(Token, null)]
    public void Starting_again_with_a_token_where_there_was_none_or_the_reverse_is_refused(string? first, string? second)
    {
        var paymentEvent = SingleOrder();
        paymentEvent.StartExecuting(paymentEvent.OnlyId(), Provider, first);
        var before = Capture(paymentEvent);

        Assert.Throws<PaymentDomainException>(() => paymentEvent.StartExecuting(paymentEvent.OnlyId(), Provider, second));
        Assert.Equal(before, Capture(paymentEvent));
    }

    [Fact]
    public void Starting_again_with_a_different_token_is_refused_and_keeps_the_original()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Executing);
        var before = Capture(paymentEvent);

        Assert.Throws<PaymentDomainException>(() => paymentEvent.StartExecuting(paymentEvent.OnlyId(), Provider, "another"));
        Assert.Equal(before, Capture(paymentEvent));
    }

    [Theory]
    [InlineData(PaymentOrderStatus.Success)]
    [InlineData(PaymentOrderStatus.Failed)]
    public void A_settled_order_keeps_its_token_when_a_new_start_is_refused(PaymentOrderStatus status)
    {
        var paymentEvent = SingleOrderIn(status);

        Assert.Throws<PaymentDomainException>(() => paymentEvent.StartExecuting(paymentEvent.OnlyId(), Provider, "another"));
        Assert.Equal(Token, paymentEvent.Only().PspToken);
    }

    // --- Provider ---

    [Fact]
    public void Starting_records_the_provider()
    {
        var order = SingleOrderIn(PaymentOrderStatus.Executing).Only();

        Assert.Equal(Provider, order.Provider);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Refuses_a_blank_provider_and_changes_nothing(string provider)
    {
        var paymentEvent = SingleOrder();
        var before = Capture(paymentEvent);

        var thrown = Record.Exception(() => paymentEvent.StartExecuting(paymentEvent.OnlyId(), provider, Token));

        Assert.IsType<PaymentDomainException>(thrown);
        Assert.Equal(before, Capture(paymentEvent));
    }

    // Provider is varchar(20): a longer name would pass the domain and fail the save after the PSP session exists.
    [Fact]
    public void Refuses_a_provider_longer_than_the_column_and_changes_nothing()
    {
        var paymentEvent = SingleOrder();
        var before = Capture(paymentEvent);

        var thrown = Record.Exception(() =>
            paymentEvent.StartExecuting(paymentEvent.OnlyId(), new string('p', PaymentOrder.ProviderMaxLength + 1), Token));

        Assert.IsType<PaymentDomainException>(thrown);
        Assert.Equal(before, Capture(paymentEvent));
    }

    // The same reference at a second provider is still a second payment session.
    [Fact]
    public void Starting_again_with_the_same_token_at_another_provider_is_refused_and_keeps_the_original()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Executing);
        var before = Capture(paymentEvent);

        Assert.Throws<PaymentDomainException>(() => paymentEvent.StartExecuting(paymentEvent.OnlyId(), "Braintree", Token));
        Assert.Equal(before, Capture(paymentEvent));
    }

    // --- Settlement and domain events ---

    [Fact]
    public void Succeeding_raises_one_event_even_when_repeated()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Executing);
        var id = paymentEvent.OnlyId();

        paymentEvent.SucceedOrder(id);
        paymentEvent.SucceedOrder(id);
        paymentEvent.SucceedOrder(id);

        var succeeded = Assert.IsType<PaymentOrderSucceededDomainEvent>(Assert.Single(paymentEvent.DomainEvents));
        Assert.Equal(id, succeeded.PaymentOrderId);
        Assert.Equal(paymentEvent.CheckoutId, succeeded.CheckoutId);
    }

    [Fact]
    public void Failing_raises_one_event_even_when_repeated()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Executing);
        var id = paymentEvent.OnlyId();

        paymentEvent.FailOrder(id);
        paymentEvent.FailOrder(id);
        paymentEvent.FailOrder(id);

        var failed = Assert.IsType<PaymentOrderFailedDomainEvent>(Assert.Single(paymentEvent.DomainEvents));
        Assert.Equal(id, failed.PaymentOrderId);
        Assert.Equal(paymentEvent.CheckoutId, failed.CheckoutId);
    }

    [Fact]
    public void Settlement_events_carry_only_ids()
    {
        foreach (var type in new[] { typeof(PaymentOrderSucceededDomainEvent), typeof(PaymentOrderFailedDomainEvent) })
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.DeclaringType == type)
                .ToList();

            Assert.All(properties, property => Assert.Equal(typeof(Guid), property.PropertyType));
            Assert.Equal(["CheckoutId", "PaymentOrderId"], properties.Select(property => property.Name).Order());
        }
    }

    // The dispatcher clears events before publishing; a redelivered callback after that must not re-publish.
    [Fact]
    public void A_redelivered_success_after_dispatch_raises_nothing()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Success);
        paymentEvent.ClearDomainEvents();

        paymentEvent.SucceedOrder(paymentEvent.OnlyId());

        Assert.Empty(paymentEvent.DomainEvents);
        Assert.Equal(PaymentOrderStatus.Success, paymentEvent.Only().Status);
    }

    [Fact]
    public void A_redelivered_failure_after_dispatch_raises_nothing()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Failed);
        paymentEvent.ClearDomainEvents();

        paymentEvent.FailOrder(paymentEvent.OnlyId());

        Assert.Empty(paymentEvent.DomainEvents);
        Assert.Equal(PaymentOrderStatus.Failed, paymentEvent.Only().Status);
    }

    [Fact]
    public void A_late_failure_cannot_undo_a_success()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Success);
        var before = Capture(paymentEvent);

        Assert.Throws<PaymentDomainException>(() => paymentEvent.FailOrder(paymentEvent.OnlyId()));
        Assert.Equal(before, Capture(paymentEvent));
    }

    [Fact]
    public void A_late_success_cannot_undo_a_failure()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Failed);
        var before = Capture(paymentEvent);

        Assert.Throws<PaymentDomainException>(() => paymentEvent.SucceedOrder(paymentEvent.OnlyId()));
        Assert.Equal(before, Capture(paymentEvent));
    }

    [Fact]
    public void A_late_failure_after_dispatch_still_cannot_undo_a_success()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Success);
        paymentEvent.ClearDomainEvents();

        Assert.Throws<PaymentDomainException>(() => paymentEvent.FailOrder(paymentEvent.OnlyId()));
        Assert.Empty(paymentEvent.DomainEvents);
        Assert.Equal(PaymentOrderStatus.Success, paymentEvent.Only().Status);
        Assert.True(paymentEvent.IsPaymentDone);
    }

    [Fact]
    public void The_events_read_earlier_do_not_change_when_the_checkout_does()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Executing);
        var earlier = paymentEvent.DomainEvents;

        paymentEvent.SucceedOrder(paymentEvent.OnlyId());

        Assert.Empty(earlier);
        Assert.Single(paymentEvent.DomainEvents);
    }

    [Fact]
    public void The_events_cannot_be_mutated_through_the_collection_they_are_read_from()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Success);
        var events = (ICollection<DomainEvent>)paymentEvent.DomainEvents;

        Assert.ThrowsAny<NotSupportedException>(() => events.Clear());
        Assert.ThrowsAny<NotSupportedException>(() => events.Add(new PaymentOrderFailedDomainEvent(paymentEvent.OnlyId(), paymentEvent.CheckoutId)));
        Assert.IsType<PaymentOrderSucceededDomainEvent>(Assert.Single(paymentEvent.DomainEvents));
    }

    // A public AddDomainEvent would let any caller make a successful checkout announce that an order failed.
    [Fact]
    public void Only_the_aggregate_itself_can_raise_its_domain_events()
    {
        var addDomainEvent = typeof(Entity).GetMethod("AddDomainEvent", BindingFlags.Public | BindingFlags.Instance);

        Assert.Null(addDomainEvent);
    }

    // --- Reconciliation flags ---

    [Fact]
    public void Reconciliation_flags_are_set_on_a_successful_order()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Success);
        var id = paymentEvent.OnlyId();

        paymentEvent.MarkWalletUpdated(id);
        paymentEvent.MarkLedgerUpdated(id);

        Assert.True(paymentEvent.Only().WalletUpdated);
        Assert.True(paymentEvent.Only().LedgerUpdated);
    }

    [Fact]
    public void Setting_a_reconciliation_flag_twice_is_a_no_op()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Success);
        var id = paymentEvent.OnlyId();
        paymentEvent.MarkWalletUpdated(id);
        paymentEvent.MarkLedgerUpdated(id);
        var before = Capture(paymentEvent);

        paymentEvent.MarkWalletUpdated(id);
        paymentEvent.MarkLedgerUpdated(id);

        Assert.Equal(before, Capture(paymentEvent));
    }

    [Fact]
    public void Setting_reconciliation_flags_raises_no_events_and_keeps_the_status()
    {
        var paymentEvent = SingleOrderIn(PaymentOrderStatus.Success);
        var id = paymentEvent.OnlyId();
        paymentEvent.ClearDomainEvents();

        paymentEvent.MarkWalletUpdated(id);
        paymentEvent.MarkLedgerUpdated(id);

        Assert.Empty(paymentEvent.DomainEvents);
        Assert.Equal(PaymentOrderStatus.Success, paymentEvent.Only().Status);
        Assert.True(paymentEvent.IsPaymentDone);
    }

    [Theory]
    [InlineData(PaymentOrderStatus.NotStarted)]
    [InlineData(PaymentOrderStatus.Executing)]
    [InlineData(PaymentOrderStatus.Failed)]
    public void Reconciliation_flags_are_never_set_on_an_order_that_did_not_succeed(PaymentOrderStatus status)
    {
        var paymentEvent = SingleOrderIn(status);
        var id = paymentEvent.OnlyId();
        var before = Capture(paymentEvent);

        Assert.Throws<PaymentDomainException>(() => paymentEvent.MarkWalletUpdated(id));
        Assert.Throws<PaymentDomainException>(() => paymentEvent.MarkLedgerUpdated(id));
        Assert.Equal(before, Capture(paymentEvent));
    }
}
