using Bookings.Application.Commands;
using Bookings.Application.Dtos;
using Bookings.Application.Exceptions;
using Bookings.Application.Locking;
using Bookings.Application.Services.Interfaces;
using Bookings.Domain.Abstractions;
using Bookings.Domain.Entities;
using Bookings.Domain.Exceptions;
using Bookings.Domain.Enums;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers;

internal class MakeBookingCommandHandler : IRequestHandler<MakeBookingCommand, long>
{
    private readonly IBookingRepository _bookingRepository;
    private readonly ITicketsRepository _ticketsRepository;
    private readonly ICacheService _cacheService;
    private readonly IAfterCommitQueue _afterCommit;
    private const int TicketCountConfig = 2;

    public MakeBookingCommandHandler(IBookingRepository bookingRepository,
        ITicketsRepository ticketsRepository,
        ICacheService cacheService,
        IAfterCommitQueue afterCommit)
    {
        _bookingRepository = bookingRepository;
        _ticketsRepository = ticketsRepository;
        _cacheService = cacheService;
        _afterCommit = afterCommit;
    }

    public async Task<long> Handle(MakeBookingCommand request, CancellationToken cancellationToken)
    {
        var ticketIds = await GetValidTicketIdsAsync(request.Tickets,
            request.EventId,
            request.UserId,
            cancellationToken);

        // Create raises BookingCreatedDomainEvent itself, and the handler for that event is what
        // marks these tickets as booked.
        var booking = Booking.Create(request.UserId, BookingStatus.Booked, ticketIds);

        await _bookingRepository.AddAsync(booking);
        await _bookingRepository.SaveChangesAsync(cancellationToken);

        // The reservation has done its job: the seats are held by the tickets' own status now, which
        // is durable where the reservation was not. Queued rather than deleted here because this runs
        // inside the transaction and Redis would not roll back with it — if the booking fails after
        // this point the user keeps the reservation they still hold and can try again.
        var reservationKeys = ticketIds.Select(ReservationKeys.Reservation).ToArray();
        _afterCommit.Enqueue(_ => _cacheService.RemoveAsync(reservationKeys));

        // Populated by the database during the save above.
        return booking.Id;
    }

    private async Task<long[]> GetValidTicketIdsAsync(long[] ticketIds,
        string eventId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (ticketIds.Length == 0)
            throw new BookingsDomainException("Select tickets to book");

        if (ticketIds.Length > TicketCountConfig)
            throw new BookingsDomainException("Too many tickets");

        var keys = ticketIds.Select(ReservationKeys.Reservation).ToArray();
        var reservedTickets = await _cacheService.GetByKeysAsync<ReserveTicketDto>(keys);

        // Every ticket has to still be reserved, not merely one of them. A reservation that has partly
        // expired is not something to book the remainder of.
        if (reservedTickets.Count != ticketIds.Length)
            throw new BookingsApplicationException("No reserved tickets found");

        if (reservedTickets.Any(x => !ticketIds.Contains(x.TicketId)))
            throw new BookingsApplicationException("Some of the tickets are not reserved");

        if (reservedTickets.Any(x => x.EventId != eventId))
            throw new BookingsDomainException("Wrong event");

        if (reservedTickets.Any(x => x.UserId != userId))
            throw new BookingsApplicationException("Those tickets are reserved by somebody else");

        var tickets = await _ticketsRepository.GetTicketsForBookingAsync([..ticketIds],
            eventId,
            cancellationToken);

        // Every requested ticket has to be bookable, not just some of them. Returning the subset
        // would create a booking for fewer seats than the user reserved and asked for.
        if (tickets.Length != ticketIds.Length)
            throw new BookingsApplicationException("Some of the tickets are no longer available");

        return tickets.Select(ticket => ticket.Id).ToArray();
    }
}