using Bookings.Application.Commands;
using Bookings.Application.Commands.Bookings;
using Bookings.Domain.Abstractions;
using Bookings.Domain.Entities;
using Bookings.Sql;
using Bookings.Sql.Pipelines;
using BookingIntegration.Fixtures;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingIntegration.Mechanics;

/// <summary>
/// What the transaction behavior does around a handler, against a real provider: commit on success,
/// discard on failure, and stand aside when something else already owns the transaction.
/// <para>
/// That last case is not hypothetical. A command sent from a Wolverine message handler arrives with
/// Wolverine's Entity Framework middleware already holding a transaction on this context, because
/// that is what puts the outgoing message and the write in the same one. Opening a second
/// transaction there fails, and committing the outer one early would break exactly the guarantee it
/// exists to provide.
/// </para>
/// </summary>
public sealed class TransactionBehaviorTests : IntegrationTest
{
    public TransactionBehaviorTests(BookingsFixture fixture) : base(fixture)
    {
    }

    private BookingDomainContext Context => Act.GetRequiredService<BookingDomainContext>();

    private IAfterCommitQueue AfterCommit => Act.GetRequiredService<IAfterCommitQueue>();

    private static readonly MakeBookingCommand ACommand =
        new("user-1", "event-1", [1L]);

    private Task<long> Run(RequestHandlerDelegate<long> handler) =>
        new TransactionBehavior<MakeBookingCommand, long>(Context,
                AfterCommit,
                NullLogger<TransactionBehavior<MakeBookingCommand, long>>.Instance)
            .Handle(ACommand, handler, CancellationToken.None);

    private Task AddATicketAsync()
    {
        Context.Tickets.Add(new Ticket("A1", "venue-1", "event-1", Seed.Soon));
        return Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Commits_what_the_handler_wrote()
    {
        await Run(async _ =>
        {
            await AddATicketAsync();
            return 1L;
        });

        Context.ChangeTracker.Clear();
        Assert.Equal(1, await Context.Tickets.CountAsync());
    }

    [Fact]
    public async Task Discards_what_the_handler_wrote_when_it_throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(async _ =>
        {
            await AddATicketAsync();
            throw new InvalidOperationException("handler failed");
        }));

        Context.ChangeTracker.Clear();
        Assert.Equal(0, await Context.Tickets.CountAsync());
    }

    [Fact]
    public async Task Rethrows_the_handler_failure_rather_than_swallowing_it()
    {
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Run(_ => throw new InvalidOperationException("handler failed")));

        Assert.Equal("handler failed", thrown.Message);
    }

    /// <summary>
    /// Standing aside means two things, both asserted: it must not throw trying to open a second
    /// transaction, and it must leave the outer one open for its owner to commit.
    /// </summary>
    [Fact]
    public async Task Defers_to_a_transaction_that_is_already_open()
    {
        await using var outer = await Context.Database.BeginTransactionAsync();

        await Run(async _ =>
        {
            await AddATicketAsync();
            return 1L;
        });

        Assert.NotNull(Context.Database.CurrentTransaction);
        Assert.Equal(outer.TransactionId, Context.Database.CurrentTransaction!.TransactionId);
    }

    /// <summary>
    /// And the owner's rollback still has to reach the handler's write — proof the behavior really
    /// joined that transaction rather than quietly committing outside it.
    /// </summary>
    [Fact]
    public async Task Work_it_deferred_on_is_still_the_owner_s_to_roll_back()
    {
        await using (var outer = await Context.Database.BeginTransactionAsync())
        {
            await Run(async _ =>
            {
                await AddATicketAsync();
                return 1L;
            });

            await outer.RollbackAsync();
        }

        Context.ChangeTracker.Clear();
        Assert.Equal(0, await Context.Tickets.CountAsync());
    }

    // --- After-commit work ---

    /// <summary>
    /// The point of queuing: work aimed at somewhere that does not roll back must not run until the
    /// database transaction is genuinely finished. Asserted by having the work observe that the
    /// transaction is gone by the time it runs.
    /// </summary>
    [Fact]
    public async Task Runs_queued_work_only_once_the_transaction_has_gone()
    {
        var transactionWhenItRan = new List<bool>();

        await Run(async _ =>
        {
            await AddATicketAsync();
            AfterCommit.Enqueue(_ =>
            {
                transactionWhenItRan.Add(Context.Database.CurrentTransaction is not null);
                return Task.CompletedTask;
            });
            return 1L;
        });

        Assert.Equal([false], transactionWhenItRan);
    }

    [Fact]
    public async Task Does_not_run_queued_work_when_the_handler_fails()
    {
        var ran = false;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(_ =>
        {
            AfterCommit.Enqueue(_ =>
            {
                ran = true;
                return Task.CompletedTask;
            });
            throw new InvalidOperationException("handler failed");
        }));

        Assert.False(ran);
    }

    /// <summary>
    /// The commit already happened, so the request succeeded. Reporting it as failed would invite a
    /// retry of work that is already done; the cleanup is left for a TTL to cover.
    /// </summary>
    [Fact]
    public async Task A_failure_in_queued_work_does_not_fail_the_request()
    {
        await Run(async _ =>
        {
            await AddATicketAsync();
            AfterCommit.Enqueue(_ => throw new InvalidOperationException("redis is down"));
            return 1L;
        });

        Context.ChangeTracker.Clear();
        Assert.Equal(1, await Context.Tickets.CountAsync());
    }

    /// <summary>
    /// This is why the behavior stands aside instead of opening its own transaction: a second one on
    /// the same context is not something EF permits. Without deferring, every command sent from a
    /// Wolverine message handler — all the catalogue sync and payment paths — would fail right here.
    /// <para>
    /// Npgsql's message is "The connection is already in a transaction." — asserted on the exception
    /// type plus this stable substring rather than the full message, since it is still provider text
    /// and not a contract.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_second_transaction_on_one_context_is_not_possible()
    {
        await using var outer = await Context.Database.BeginTransactionAsync();

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Context.Database.BeginTransactionAsync());

        Assert.Contains("already in a transaction", thrown.Message);
    }

    /// <summary>
    /// The commit belongs to the owner, so this behavior has no way to know when it happened and the
    /// queued work cannot run. It says so rather than failing the request: the same lost cleanup is
    /// reported, not made fatal, on the path it does own.
    /// </summary>
    [Fact]
    public async Task Says_so_when_it_cannot_run_queued_work()
    {
        await using var outer = await Context.Database.BeginTransactionAsync();
        var ran = false;

        await Run(async _ =>
        {
            await AddATicketAsync();
            AfterCommit.Enqueue(_ =>
            {
                ran = true;
                return Task.CompletedTask;
            });
            return 1L;
        });

        Assert.False(ran);
        Assert.True(AfterCommit.HasWork);
        Assert.NotNull(Context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Defers_quietly_when_there_is_no_queued_work()
    {
        await using var outer = await Context.Database.BeginTransactionAsync();

        await Run(async _ =>
        {
            await AddATicketAsync();
            return 1L;
        });

        Assert.NotNull(Context.Database.CurrentTransaction);
    }
}
