using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;

namespace PaymentIntegration.Persistence;

// Postgres rounds anything past numeric(18,2) silently, so the amount a PSP charged and the one stored must be the
// same digits; these read back through a fresh scope to see what the column holds.
public sealed class PrecisionTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Theory]
    [InlineData("0.01")]
    [InlineData("19.99")]
    [InlineData("9999999999999999.99")]
    public async Task Amounts_round_trip_exactly(string value)
    {
        var amount = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        var checkout = await SeedCheckoutAsync(OrderState.Success, amount: amount);
        var order = checkout.PaymentOrders.Single();
        var wallet = await SeedWalletAsync(order.SellerId, amount);
        var refund = await SeedRefundAsync(order.PaymentOrderId, amount);

        var stored = await ReadCheckoutAsync(checkout.BookingId);
        Assert.Equal(amount, stored.TotalAmount.Amount);
        Assert.Equal(amount, stored.FindOrder(order.PaymentOrderId).Amount.Amount);
        Assert.Equal(amount, (await ReadAsync(c => c.Wallets.SingleAsync(w => w.WalletId == wallet.WalletId))).CurrentBalance.Amount);
        Assert.Equal(amount, (await ReadAsync(c => c.RefundOrders.SingleAsync(r => r.RefundOrderId == refund.RefundOrderId))).RefundAmount.Amount);
    }
}
