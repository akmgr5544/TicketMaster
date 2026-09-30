using System.Globalization;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Enums;

namespace PaymentDomain;

public class LedgerEntryTests
{
    private static PaymentOrder SucceededOrder(decimal amount = 42.10m, string currency = "EUR")
    {
        var paymentEvent = Checkouts.SingleOrder(amount, currency);
        paymentEvent.Drive(paymentEvent.OnlyId(), PaymentOrderStatus.Success);
        return paymentEvent.Only();
    }

    private static decimal Net(IEnumerable<LedgerEntry> entries) =>
        entries.Sum(entry => entry.Type == EntryType.Credit ? entry.Amount : -entry.Amount);

    [Fact]
    public void A_pay_in_debits_the_buyer_and_credits_the_seller()
    {
        var order = SucceededOrder();

        var entries = LedgerEntry.RecordPayIn(order);

        Assert.Equal(2, entries.Count);
        var debit = Assert.Single(entries, entry => entry.Type == EntryType.Debit);
        var credit = Assert.Single(entries, entry => entry.Type == EntryType.Credit);
        Assert.Equal(order.BuyerId, debit.AccountId);
        Assert.Equal(order.MerchantId, credit.AccountId);
        Assert.All(entries, entry =>
        {
            Assert.Equal(order.PaymentOrderId, entry.PaymentOrderId);
            Assert.Equal(order.Amount, entry.Amount);
            Assert.Equal("EUR", entry.Currency);
        });
    }

    [Fact]
    public void A_pay_in_sums_to_zero()
    {
        Assert.Equal(0m, Net(LedgerEntry.RecordPayIn(SucceededOrder())));
    }

    [Theory]
    [InlineData("0.01", "USD")]
    [InlineData("1", "JPY")]
    [InlineData("123456.78", "GBP")]
    [InlineData("9999999999999999.99", "EUR")]
    public void A_pay_in_balances_and_carries_the_order_amount_for_any_amount(string amount, string currency)
    {
        var order = SucceededOrder(decimal.Parse(amount, CultureInfo.InvariantCulture), currency);

        var entries = LedgerEntry.RecordPayIn(order);

        Assert.Equal(0m, Net(entries));
        Assert.All(entries, entry =>
        {
            Assert.True(entry.Amount > 0m);
            Assert.Equal(order.Amount, entry.Amount);
            Assert.Equal(currency, entry.Currency);
        });
    }

    [Fact]
    public void The_two_sides_of_a_pay_in_are_different_accounts()
    {
        var entries = LedgerEntry.RecordPayIn(SucceededOrder());

        Assert.NotEqual(entries[0].AccountId, entries[1].AccountId);
        Assert.All(entries, entry => Assert.NotEqual(Guid.Empty, entry.AccountId));
    }

    [Fact]
    public void Recording_a_pay_in_does_not_change_the_checkout()
    {
        var paymentEvent = Checkouts.SingleOrderIn(PaymentOrderStatus.Success);
        var before = Checkouts.Capture(paymentEvent);

        LedgerEntry.RecordPayIn(paymentEvent.Only());

        Assert.Equal(before, Checkouts.Capture(paymentEvent));
    }

    [Theory]
    [InlineData(PaymentOrderStatus.NotStarted)]
    [InlineData(PaymentOrderStatus.Executing)]
    [InlineData(PaymentOrderStatus.Failed)]
    public void Refuses_an_order_that_has_not_succeeded(PaymentOrderStatus status)
    {
        var order = Checkouts.SingleOrderIn(status).Only();

        Assert.Throws<PaymentDomainException>(() => LedgerEntry.RecordPayIn(order));
    }

    [Fact]
    public void Each_order_of_a_multi_seller_checkout_debits_the_same_buyer_and_credits_its_own_seller()
    {
        var paymentEvent = Checkouts.WithLines(Checkouts.Line(10m), Checkouts.Line(20m, "EUR"));
        foreach (var id in paymentEvent.Ids())
            paymentEvent.Drive(id, PaymentOrderStatus.Success);

        foreach (var order in paymentEvent.PaymentOrders)
        {
            var entries = LedgerEntry.RecordPayIn(order);

            Assert.Equal(paymentEvent.BuyerId, Assert.Single(entries, entry => entry.Type == EntryType.Debit).AccountId);
            Assert.Equal(order.MerchantId, Assert.Single(entries, entry => entry.Type == EntryType.Credit).AccountId);
            Assert.Equal(0m, Net(entries));
        }
    }

    [Fact]
    public void A_missing_order_is_refused_as_an_argument_error_not_a_null_dereference()
    {
        var thrown = Record.Exception(() => LedgerEntry.RecordPayIn(null!));

        Assert.NotNull(thrown);
        Assert.True(thrown is ArgumentNullException or PaymentDomainException,
            $"Expected ArgumentNullException or PaymentDomainException, got {thrown.GetType().Name}.");
    }

    // The returned list is what gets appended; a caller should not be able to smuggle an extra entry into it.
    [Fact]
    public void The_recorded_pair_cannot_be_extended_by_the_caller()
    {
        var entries = LedgerEntry.RecordPayIn(SucceededOrder());

        if (entries is ICollection<LedgerEntry> { IsReadOnly: false } collection)
        {
            Assert.ThrowsAny<NotSupportedException>(() => collection.Add(entries[0]));
        }

        Assert.Equal(2, entries.Count);
    }
}
