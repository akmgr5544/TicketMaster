using System.Net;
using System.Net.Http.Json;
using Npgsql;
using Users.Api.Entities;
using UsersIntegration.Fixtures;

namespace UsersIntegration.Features.Register;

public sealed class FirstAdminTests : UsersTest
{
    public FirstAdminTests(UsersHostFixture fixture) : base(fixture)
    {
    }

    [Fact]
    public async Task The_first_account_ever_registered_is_an_Admin()
    {
        var tokens = await RegisterAsync("first");

        Assert.Equal(UserRole.Admin, (await StoredUserAsync("first")).Role);
        Assert.Equal(nameof(UserRole.Admin), RoleClaimOf(tokens.Token));
    }

    [Fact]
    public async Task Every_account_after_the_first_is_a_Customer()
    {
        await RegisterAsync("first");
        var second = await RegisterAsync("second");
        await RegisterAsync("third");

        Assert.Equal(UserRole.Customer, (await StoredUserAsync("second")).Role);
        Assert.Equal(UserRole.Customer, (await StoredUserAsync("third")).Role);
        Assert.Equal(nameof(UserRole.Customer), RoleClaimOf(second.Token));
    }

    [Fact]
    public async Task A_rejected_first_registration_does_not_use_up_the_Admin_slot()
    {
        using var rejected = await Client.PostAsJsonAsync("api/users/registration", new
        {
            userName = "typo",
            email = "typo@example.com",
            password = Password,
            confirmPassword = Password + "x",
            firstName = "First",
            lastName = "Last",
            phoneNumber = "+10000000000"
        });
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);

        await RegisterAsync("first");

        Assert.Equal(UserRole.Admin, (await StoredUserAsync("first")).Role);
    }

    // Characterises the race the users-service skill accepts for bootstrap: the "is the table empty?" check
    // and the insert are separate statements with no lock or constraint between them, so two registrations
    // that both read the empty table both become Admin. Determinism comes from a table lock that admits the
    // checks (ACCESS SHARE) but holds back the inserts (ROW EXCLUSIVE) until both are waiting on it. If a
    // guard is ever added, this test goes red and should be inverted to assert exactly one Admin.
    [Fact]
    public async Task Two_first_registrations_that_both_see_an_empty_table_both_become_Admin()
    {
        await using var gate = new NpgsqlConnection(Fixture.ConnectionString);
        await gate.OpenAsync();
        await using var hold = await gate.BeginTransactionAsync();
        await using (var lockTable = new NpgsqlCommand("""LOCK TABLE "Users" IN SHARE ROW EXCLUSIVE MODE""", gate, hold))
            await lockTable.ExecuteNonQueryAsync();

        var alice = PostRegistrationAsync("alice");
        var bob = PostRegistrationAsync("bob");

        await WaitForBlockedInsertsAsync(expected: 2);
        await hold.RollbackAsync();

        using var aliceResponse = await alice;
        using var bobResponse = await bob;
        Assert.Equal(HttpStatusCode.OK, aliceResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, bobResponse.StatusCode);

        Assert.Equal(UserRole.Admin, (await StoredUserAsync("alice")).Role);
        Assert.Equal(UserRole.Admin, (await StoredUserAsync("bob")).Role);
    }

    // Polled from its own connection: inside the gate's transaction pg_stat_activity is a snapshot frozen at
    // its first read, and would never see the inserts arrive.
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
