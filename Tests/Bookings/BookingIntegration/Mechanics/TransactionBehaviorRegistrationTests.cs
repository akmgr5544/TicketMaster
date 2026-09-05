using Bookings.Application.Commands;
using Bookings.Application.Commands.Bookings;
using Bookings.Application.Commands.Tickets;
using Bookings.Domain.Abstractions;
using Bookings.Sql.Pipelines;
using BookingIntegration.Fixtures;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace BookingIntegration.Mechanics;

/// <summary>
/// <c>TransactionBehavior</c> is registered open-generic, so on its own it wraps every request the
/// service sends — including ones that only touch Redis, for which a Postgres transaction is pure
/// overhead and rolls nothing back. Constraining it to <c>ITransactionalRequest</c> is what scopes it.
/// <para>
/// This asserts against the fixture's provider, which called <c>AddInfrastructureServices</c> for
/// real, because the scoping only works if dependency injection skips an open-generic registration
/// whose constraints the requested type arguments do not satisfy, instead of failing to build it. A
/// hand-copied registration would stop mirroring the real one the day somebody edits one and not the
/// other.
/// </para>
/// <para>
/// The counts below filter to <see cref="TransactionBehavior{TRequest,TResponse}"/> specifically,
/// rather than asserting the raw registration count, because the fixture also registers
/// <c>AfterHandlerFailureBehavior</c> — a second, test-only, <c>ITransactionalRequest</c>-constrained
/// behavior <c>MakeBookingTests</c> uses to inject an in-transaction failure. That is a deliberate
/// addition to the test DI graph, not a production duplicate, so a raw "exactly one" count would be
/// asserting something no longer true of this fixture for the right reason.
/// </para>
/// </summary>
public sealed class TransactionBehaviorRegistrationTests : IntegrationTest
{
    public TransactionBehaviorRegistrationTests(BookingsFixture fixture) : base(fixture)
    {
    }

    private IPipelineBehavior<TRequest, Unit>[] BehaviorsFor<TRequest>()
        where TRequest : IRequest =>
        Act.GetServices<IPipelineBehavior<TRequest, Unit>>().ToArray();

    private TransactionBehavior<TRequest, Unit>[] TransactionBehaviorsFor<TRequest>()
        where TRequest : notnull, IRequest, ITransactionalRequest =>
        Act.GetServices<IPipelineBehavior<TRequest, Unit>>().OfType<TransactionBehavior<TRequest, Unit>>().ToArray();

    private TransactionBehavior<TRequest, TResponse>[] TransactionBehaviorsFor<TRequest, TResponse>()
        where TRequest : notnull, IRequest<TResponse>, ITransactionalRequest =>
        Act.GetServices<IPipelineBehavior<TRequest, TResponse>>()
            .OfType<TransactionBehavior<TRequest, TResponse>>()
            .ToArray();

    [Fact]
    public void Wraps_a_command_that_writes_to_the_database()
    {
        Assert.Single(TransactionBehaviorsFor<MakeBookingCommand, long>());
    }

    [Fact]
    public void Wraps_the_commands_that_reconcile_tickets_with_the_catalogue()
    {
        Assert.Single(TransactionBehaviorsFor<CancelEventTicketsCommand>());
        Assert.Single(TransactionBehaviorsFor<RescheduleEventTicketsCommand>());
        Assert.Single(TransactionBehaviorsFor<ReconcileEventVenueCommand>());
    }

    /// <summary>
    /// Creating a ticket asks Events before it writes. A transaction opened here would be held open
    /// across that network call, so the command is deliberately not <c>ITransactionalRequest</c>.
    /// </summary>
    [Fact]
    public void Leaves_the_admin_ticket_create_alone()
    {
        Assert.Empty(BehaviorsFor<CreateTicketCommand>());
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
