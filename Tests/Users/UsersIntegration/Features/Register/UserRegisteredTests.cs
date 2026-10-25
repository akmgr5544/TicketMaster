using System.Net;
using Npgsql;
using UsersIntegration.Fixtures;

namespace UsersIntegration.Features.Register;

// Other services keep their own copy of a user, so they must hear about exactly the accounts that exist: every
// registration that commits, and none that does not.
public sealed class UserRegisteredTests : UsersTest
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    public UserRegisteredTests(UsersHostFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task A_registration_announces_the_user_by_the_id_every_service_knows_them_by()
    {
        var userName = UniqueUserName();

        await RegisterAsync(userName);

        var published = await Fixture.Published.WaitForAsync($"{userName}@example.com", Wait);
        Assert.Equal((await StoredUserAsync(userName)).Id, published.UserId);
    }

    [Fact]
    public async Task A_refused_registration_announces_nothing()
    {
        var userName = UniqueUserName();
        var email = $"{userName}@example.com";
        await RegisterAsync(userName);
        // The first one arriving proves messages are flowing, so the absence of a second one means something.
        await Fixture.Published.WaitForAsync(email, Wait);

        using var duplicate = await PostRegistrationAsync(userName);
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Single(Fixture.Published.For(email));
    }

    // The duplicate that matters is the one that gets past the pre-check and loses on the unique index: by then its
    // message is already staged, and only the outbox rolling back with the insert keeps it from going out. A table
    // lock admits both pre-checks but holds back both inserts until both are waiting on it.
    [Fact]
    public async Task A_registration_that_loses_a_race_to_a_duplicate_announces_nothing()
    {
        var userName = UniqueUserName();
        var email = $"{userName}@example.com";

        await using var gate = new NpgsqlConnection(Fixture.ConnectionString);
        await gate.OpenAsync();
        await using var hold = await gate.BeginTransactionAsync();
        await using (var lockTable = new NpgsqlCommand("""LOCK TABLE "Users" IN SHARE ROW EXCLUSIVE MODE""", gate, hold))
            await lockTable.ExecuteNonQueryAsync();

        var first = PostRegistrationAsync(userName);
        var second = PostRegistrationAsync(userName);
        await WaitForBlockedInsertsAsync(expected: 2);
        await hold.RollbackAsync();

        using var firstResponse = await first;
        using var secondResponse = await second;
        Assert.Equal(1, new[] { firstResponse.StatusCode, secondResponse.StatusCode }.Count(status => status == HttpStatusCode.OK));

        await Fixture.Published.WaitForAsync(email, Wait);
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Single(Fixture.Published.For(email));
    }

    private static string UniqueUserName() => $"user{Guid.NewGuid():N}";

    // Polled from its own connection: inside the gate's transaction pg_stat_activity is a snapshot frozen at its
    // first read, and would never see the inserts arrive.
    private async Task WaitForBlockedInsertsAsync(int expected)
    {
        await using var probe = new NpgsqlConnection(Fixture.ConnectionString);
        await probe.OpenAsync();

        const string sql = """
            SELECT count(*) FROM pg_stat_activity
            WHERE pid <> pg_backend_pid() AND wait_event_type = 'Lock' AND query LIKE '%INSERT INTO "Users"%'
            """;

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand(sql, probe);
            if ((long)(await command.ExecuteScalarAsync())! >= expected)
                return;
            await Task.Delay(50);
        }

        throw new TimeoutException($"Expected {expected} registrations blocked on the Users insert; they never all got there.");
    }
}
