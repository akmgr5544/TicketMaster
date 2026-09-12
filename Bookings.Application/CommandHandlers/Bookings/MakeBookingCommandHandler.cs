using Bookings.Application.Dtos;
using Bookings.Application.Exceptions;
using Bookings.Application.Extensions;
using Bookings.Application.Services.Interfaces;
using Bookings.Domain.Abstractions;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Domain.Exceptions;
using Bookings.Domain.Repositories;
using MediatR;
using Bookings.Application.Commands.Bookings;

namespace Bookings.Application.CommandHandlers.Bookings;

internal sealed class MakeBookingCommandHandler : IRequestHandler<MakeBookingCommand, long>
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

        var booking = Booking.Create(request.UserId, BookingStatus.Booked, ticketIds);

        _bookingRepository.Add(booking);
        await _bookingRepository.SaveChangesAsync(cancellationToken);
        
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

        // A duplicate id would pass the reservation lookup (fewer distinct keys than ids) and then
        // trip the count mismatch below with the misleading "No reserved tickets found". Reject it
        // here with a clear message, as ReserveTicketCommandHandler does.
        if (ticketIds.Distinct().Count() != ticketIds.Length)
            throw new BookingsDomainException("The same ticket was selected more than once");

        var keys = ticketIds.Select(ReservationKeys.Reservation).ToArray();
        var reservedTickets = await _cacheService.GetByKeysAsync<ReserveTicketDto>(keys);

        if (reservedTickets.Count != ticketIds.Length)
            throw new BookingsApplicationException("No reserved tickets found");

        if (reservedTickets.Any(x => !ticketIds.Contains(x.TicketId)))
            throw new BookingsApplicationException("Some of the tickets are not reserved");

        if (reservedTickets.Any(x => x.EventId != eventId))
            throw new BookingsDomainException("Wrong event");

        if (reservedTickets.Any(x => x.UserId != userId))
            throw new BookingsApplicationException("Those tickets are reserved by somebody else");

        var tickets = await _ticketsRepository.GetTicketsByIdAsync([..ticketIds], cancellationToken);
        
        if (tickets.Length != ticketIds.Length
            || tickets.Any(ticket => !ticket.IsAvailableFor(eventId, DateTime.UtcNow)))
        {
            throw new BookingsApplicationException("Some of the tickets are no longer available");
        }

        return tickets.Select(ticket => ticket.Id).ToArray();
    }
}