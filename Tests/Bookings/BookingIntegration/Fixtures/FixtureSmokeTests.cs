using Bookings.Sql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace BookingIntegration.Fixtures;

[Collection(BookingsCollection.Name)]
public sealed class FixtureSmokeTests
{
    private readonly BookingsFixture _fixture;

    public FixtureSmokeTests(BookingsFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Migrations_have_been_applied()
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BookingDomainContext>();

        var applied = await context.Database.GetAppliedMigrationsAsync();

        Assert.NotEmpty(applied);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }

    [Fact]
    public async Task Redis_answers_and_the_reset_clears_it()
    {
        var multiplexer = _fixture.Services.GetRequiredService<IConnectionMultiplexer>();
        var db = multiplexer.GetDatabase();

        await db.StringSetAsync("smoke", "value");
        Assert.Equal("value", await db.StringGetAsync("smoke"));

        await _fixture.ResetAsync();

        Assert.False(await db.KeyExistsAsync("smoke"));
    }

    [Fact]
    public async Task Reset_leaves_the_migration_history_intact()
    {
        await _fixture.ResetAsync();

        await using var scope = _fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BookingDomainContext>();

        // Respawn ignoring __EFMigrationsHistory is what keeps this true; without it the schema is
        // still there but EF reports every migration as pending.
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
    }
}
