using Bookings.Application.Commands;
using Bookings.Application.Dtos;
using Bookings.Application.Exceptions;
using Bookings.Application.Extensions;
using Bookings.Application.Services.Interfaces;
using Bookings.Domain.Exceptions;
using Bookings.Domain.Repositories;
using Medallion.Threading;
using MediatR;
using Bookings.Application.Commands.Tickets;

namespace Bookings.Application.CommandHandlers.Tickets;

internal class ReserveTicketCommandHandler : IRequestHandler<ReserveTicketCommand>
{
    private readonly IDistributedLockProvider _lockProvider;
    private readonly ICacheService _cacheService;
    private readonly ITicketsRepository _ticketsRepository;

    private const int TicketCountConfig = 2;

    private static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(5);
    
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
        
        if (ticketIds.Distinct().Count() != ticketIds.Length)
            throw new BookingsDomainException("The same ticket was selected more than once");

        var ticketLocks = await _lockProvider.TryAcquireTicketLocksAsync(ticketIds,
            LockWaitTimeout,
            cancellationToken);

        if (ticketLocks is null)
            throw new BookingsApplicationException("Tickets reservation is in progress");

        await using (ticketLocks)
        {
            var keys = ticketIds.Select(ReservationKeys.Reservation).ToArray();
            var reservedTickets = await _cacheService.GetByKeysAsync<ReserveTicketDto>(keys);

            if (reservedTickets.Count > 0)
                throw new BookingsApplicationException("One of the tickets already reserved");
            
            await EnsureTicketsAreAvailableAsync(ticketIds, request.EventId, cancellationToken);

            var reservations = ticketIds
                .Select(ticketId => new KeyValuePair<string, ReserveTicketDto>(
                    ReservationKeys.Reservation(ticketId),
                    new ReserveTicketDto(ticketId, request.EventId, request.UserId)))
                .ToArray();

            await _cacheService.SetToCacheAsync(reservations, ReservationTtl);
        }
    }

    private async Task EnsureTicketsAreAvailableAsync(long[] ticketIds,
        string eventId,
        CancellationToken cancellationToken)
    {
        var tickets = await _ticketsRepository.GetTicketsForReservationAsync([..ticketIds], cancellationToken);

        if (tickets.Length != ticketIds.Length)
            throw new NotFoundException("Some of the selected tickets do not exist");

        if (tickets.Any(ticket => ticket.EventId != eventId))
            throw new BookingsDomainException("Some of the selected tickets belong to another event");

        if (tickets.Any(ticket => !ticket.IsAvailableFor(eventId, DateTime.UtcNow)))
            throw new BookingsApplicationException("Some of the selected tickets are no longer available");
    }
}
