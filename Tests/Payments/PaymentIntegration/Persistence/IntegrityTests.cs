using Microsoft.EntityFrameworkCore;
using Npgsql;
using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;

namespace PaymentIntegration.Persistence;

// The database's half of rules the handlers also check, proven by writing past the handlers.
public sealed class IntegrityTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    // A request and a cancellation for one booking racing: the second insert must fail, so its retry sees the first.
    [Fact]
    public async Task The_database_refuses_a_second_claim_for_one_booking()
    {
        var bookingId = NewBookingId();
        await SeedClaimAsync(BookingClaim.Requested(bookingId));

        await Assert.ThrowsAsync<DbUpdateException>(() => SeedClaimAsync(BookingClaim.Cancelled(bookingId)));
    }

    // The domain refuses this too, so the only way to reach the index is to write a row past the root.
    [Fact]
    public async Task The_database_refuses_a_second_order_for_one_seller_in_a_checkout()
    {
        var checkout = await SeedCheckoutAsync();
        var order = checkout.PaymentOrders.Single();

        var exception = await Assert.ThrowsAsync<PostgresException>(() => InScopeAsync(c => c.Database.ExecuteSqlRawAsync(
            """
            insert into "PaymentOrders" ("PaymentOrderId", "CheckoutId", "SellerId", "Amount", "Currency", "Status",
                                         "WalletUpdated", "LedgerUpdated", "CreatedAt")
            values ({0}, {1}, {2}, 10, 'USD', 'Init', false, false, now())
            """,
            Guid.NewGuid(), checkout.CheckoutId, order.SellerId)));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [Fact]
    public async Task A_payment_methods_user_id_is_stored_as_a_uuid_and_round_trips()
    {
        var user = Guid.NewGuid();
        var method = await SeedPaymentMethodAsync(user);

        var stored = await ReadAsync(c => c.PaymentMethods.SingleAsync(m => m.Id == method.Id));
        var columnType = await ReadAsync(c => c.Database
            .SqlQueryRaw<string>("select data_type as \"Value\" from information_schema.columns where table_name = 'PaymentMethods' and column_name = 'UserId'")
            .SingleAsync());

        Assert.Equal(user, stored.UserId);
        Assert.Equal("uuid", columnType);
    }
}
