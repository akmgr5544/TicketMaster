using Bookings.Application.Commands;
using Bookings.Application.Dtos;
using Bookings.Application.Exceptions;
using Bookings.Application.Locking;
using Bookings.Application.Services.Interfaces;
using Bookings.Domain.Exceptions;
using Bookings.Domain.Repositories;
using Medallion.Threading;
using MediatR;

namespace Bookings.Application.CommandHandlers;

/// <summary>
/// Holds a set of tickets for one user for a short while, so that the expensive part of buying them
/// only happens for a checkout that has already claimed its seats.
/// <para>
/// The reservation itself lives only in Redis and lapses on its own, so a checkout the user abandons
/// before booking needs no compensating action. The database is read but never written: a seat has to
/// be real and for sale before it is worth holding, and finding that out here rather than at booking
/// means the request that is wrong is the request that fails.
/// </para>
/// </summary>
internal class ReserveTicketCommandHandler : IRequestHandler<ReserveTicketCommand>
{
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ICacheService _cacheService;
    private readonly ITicketsRepository _ticketsRepository;

    private const int TicketCountConfig = 2;

    private static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Non-zero on purpose: with a lock per ticket, a request covering several seats has several
    /// chances to meet an in-flight one, and failing instantly on each would reject reservations that
    /// only needed to wait a moment.
    /// </summary>
    private static readonly TimeSpan LockWaitTimeout = TimeSpan.FromMilliseconds(250);

    public ReserveTicketCommandHandler(IDistributedLockProvider lockProvider,
        ICacheService cacheService,
        ITicketsRepository ticketsRepository)
    {
        _lockProvider = lockProvider;
        _cacheService = cacheService;
        _ticketsRepository = ticketsRepository;
    }

    public async Task Handle(ReserveTicketCommand request, CancellationToken cancellationToken)
    {
        var ticketIds = request.Tickets;

        if (ticketIds.Length == 0)
            throw new BookingsDomainException("Select tickets to book");

        if (ticketIds.Length > TicketCountConfig)
            throw new BookingsDomainException("Too many tickets");

        // Rejected rather than deduplicated: the locks are not reentrant, so a repeated id would have
        // this request waiting on a lock it already holds and then reporting that somebody else has
        // the ticket. A request asking twice for one seat is a caller bug worth surfacing.
        if (ticketIds.Distinct().Count() != ticketIds.Length)
            throw new BookingsDomainException("The same ticket was selected more than once");

        var ticketLocks = await _lockProvider.TryAcquireTicketLocksAsync(ticketIds,
            LockWaitTimeout,
            cancellationToken);

        if (ticketLocks is null)
            throw new BookingsApplicationException("Tickets reservation is in progress");

        await using (ticketLocks)
        {
            // Both the check and the write happen with every ticket's lock held. Checking outside the
            // locks would be the same race with extra steps.
            var keys = ticketIds.Select(ReservationKeys.Reservation).ToArray();
            var reservedTickets = await _cacheService.GetByKeysAsync<ReserveTicketDto>(keys);

            if (reservedTickets.Count > 0)
                throw new BookingsApplicationException("One of the tickets already reserved");

            // Only meaningful once the check above has passed: a seat nobody holds a reservation for
            // cannot be part of a booking in flight, because booking requires a reservation. So its
            // status cannot change under us here.
            await EnsureTicketsAreAvailableAsync(ticketIds, request.EventId, cancellationToken);

            var reservations = ticketIds
                .Select(ticketId => new KeyValuePair<string, ReserveTicketDto>(
                    ReservationKeys.Reservation(ticketId),
                    new ReserveTicketDto(ticketId, request.EventId, request.UserId)))
                .ToArray();

            await _cacheService.SetToCacheAsync(reservations, ReservationTtl);
        }
    }

    /// <summary>
    /// All of them or none: a request for two seats where one is unavailable holds neither, so the
    /// user is not left half-way through a checkout they cannot finish.
    /// </summary>
    private async Task EnsureTicketsAreAvailableAsync(long[] ticketIds,
        string eventId,
        CancellationToken cancellationToken)
    {
        var tickets = await _ticketsRepository.GetTicketsForReservationAsync([..ticketIds], cancellationToken);

        if (tickets.Length != ticketIds.Length)
            throw new NotFoundException("Some of the selected tickets do not exist");

        // Separated from availability because it means the caller asked the wrong question, rather
        // than the seat having been taken.
        if (tickets.Any(ticket => ticket.EventId != eventId))
            throw new BookingsDomainException("Some of the selected tickets belong to another event");

        // Covers sold and paid-for seats, seats cancelled because the event was called off, and seats
        // for an event that has already come and gone.
        if (tickets.Any(ticket => !ticket.IsAvailableFor(eventId, DateTime.UtcNow)))
            throw new BookingsApplicationException("Some of the selected tickets are no longer available");
    }
}
