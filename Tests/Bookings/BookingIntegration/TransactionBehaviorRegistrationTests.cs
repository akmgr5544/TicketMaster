using Bookings.Application.Commands;
using Bookings.Sql;
using Bookings.Sql.Pipelines;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookingIntegration;

/// <summary>
/// <c>TransactionBehavior</c> is registered open-generic, so on its own it wraps every request the
/// service sends — including ones that only touch Redis, for which a Postgres transaction is pure
/// overhead and rolls nothing back. Constraining it to <c>ITransactionalRequest</c> is what scopes it.
/// <para>
/// This asserts against the real container rather than the type's declaration, because the scoping
/// only works if dependency injection skips an open-generic registration whose constraints the
/// requested type arguments do not satisfy, instead of failing to build it.
/// </para>
/// </summary>
public sealed class TransactionBehaviorRegistrationTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _provider = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<Bookings.Domain.Abstractions.IAfterCommitQueue, AfterCommitQueue>();
        services.AddDbContext<BookingDomainContext>(options => options.UseSqlite(_connection));

        // The registration exactly as AddInfrastructureServices makes it.
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        _provider = services.BuildServiceProvider(validateScopes: true);
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private IPipelineBehavior<TRequest, Unit>[] BehaviorsFor<TRequest>()
        where TRequest : IRequest
    {
        using var scope = _provider.CreateScope();
        return scope.ServiceProvider.GetServices<IPipelineBehavior<TRequest, Unit>>().ToArray();
    }

    private IPipelineBehavior<TRequest, TResponse>[] BehaviorsFor<TRequest, TResponse>()
        where TRequest : IRequest<TResponse>
    {
        using var scope = _provider.CreateScope();
        return scope.ServiceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>().ToArray();
    }

    [Fact]
    public void Wraps_a_command_that_writes_to_the_database()
    {
        Assert.Single(BehaviorsFor<MakeBookingCommand, long>());
    }

    [Fact]
    public void Wraps_the_commands_that_reconcile_tickets_with_the_catalogue()
    {
        Assert.Single(BehaviorsFor<CancelEventTicketsCommand>());
        Assert.Single(BehaviorsFor<RescheduleEventTicketsCommand>());
        Assert.Single(BehaviorsFor<ReconcileEventVenueCommand>());
    }

    /// <summary>
    /// Reserving only writes to Redis. Redis does not roll back with a database transaction, so
    /// wrapping it in one is misleading as well as wasteful.
    /// </summary>
    [Fact]
    public void Leaves_a_reservation_alone()
    {
        Assert.Empty(BehaviorsFor<ReserveTicketCommand>());
    }
}
