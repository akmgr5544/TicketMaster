using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;

namespace PaymentIntegration.Persistence;

// Each rule below is the database's half of a guarantee the handlers also check: the handler's check answers the
// common case, the index the race between two writers.
public sealed class SchemaTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Theory]
    [InlineData("PaymentEvents", "IX_PaymentEvents_BookingId", "CREATE UNIQUE INDEX")]                  // one checkout per booking
    [InlineData("RefundOrders", "IX_RefundOrders_PaymentOrderId_RequestId", "CREATE UNIQUE INDEX")]     // one refund per order per request
    [InlineData("PaymentMethods", "IX_PaymentMethods_UserId_Default", "WHERE \"IsDefault\"")]           // one default method per user
    [InlineData("Wallets", "IX_Wallets_UserId_Currency", "CREATE UNIQUE INDEX")]                        // one wallet per user per currency
    public async Task The_migrated_schema_holds_each_uniqueness_rule(string table, string index, string expected)
    {
        var definition = await ReadAsync(c => c.Database
            .SqlQueryRaw<string>("select indexdef as \"Value\" from pg_indexes where tablename = {0} and indexname = {1}", table, index)
            .SingleOrDefaultAsync());

        Assert.NotNull(definition);
        Assert.Contains(expected, definition);
    }
}
