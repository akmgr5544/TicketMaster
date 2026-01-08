using Bookings.Application.Commands;
using Bookings.Application.Dtos;
using Bookings.Application.Exceptions;
using Bookings.Application.Services.Interfaces;
using Bookings.Domain.DomainEvents;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers;

internal class MakeBookingCommandHandler : IRequestHandler<MakeBookingCommand>
{
    private readonly IBookingRepository _bookingRepository;
    private readonly ITicketsRepository _ticketsRepository;
    private readonly ICacheService _cacheService;
    private const int TicketCountConfig = 2;

    public MakeBookingCommandHandler(IBookingRepository bookingRepository,
        ITicketsRepository ticketsRepository,
        ICacheService cacheService)
    {
        _bookingRepository = bookingRepository;
        _ticketsRepository = ticketsRepository;
        _cacheService = cacheService;
    }

    public async Task Handle(MakeBookingCommand request, CancellationToken cancellationToken)
    {
        var tickets = await GetValidTicketsAsync(request.Tickets,
            request.EventId,
            request.UserId,
            cancellationToken);

        var booking = Booking.Create(request.UserId, BookingStatus.Booked, request.Tickets.Length);

        foreach (var ticket in tickets)
        {
            booking.AddBookedTicket(ticket.Id);
        }

        booking.AddDomainEvent(new BookingCreatedDomainEvent(tickets));

        await _bookingRepository.AddAsync(booking);
        await _bookingRepository.SaveChangesAsync(cancellationToken);
    }

    private async Task<Ticket[]> GetValidTicketsAsync(long[] ticketIds,
        string eventId,
        string userId,
        CancellationToken cancellationToken)
    {
        if (ticketIds.Length == 0)
            throw new BookingException("Select tickets to book");

        if (ticketIds.Length > TicketCountConfig)
            throw new BookingException("Too many tickets");

        var keys = ticketIds.Select(id => id.ToString()).ToArray();
        var reservedTickets = await _cacheService.GetByKeysAsync<ReserveTicketDto>(keys);
        if (reservedTickets.Count == 0)
            throw new BookingException("No reserved tickets found");

        if (reservedTickets.Any(x => !ticketIds.Contains(x.TicketId)))
            throw new BookingException("Some of the tickets don't reserved");

        if (reservedTickets.Any(x => x.EventId != eventId))
            throw new BookingException("Wrong event");

        if (reservedTickets.Any(x => x.UserId != userId))
            throw new BookingException("Wrong user");

        var tickets = await _ticketsRepository.GetTicketsForBookingAsync([..ticketIds],
            eventId,
            cancellationToken);

        if (tickets.Length == 0)
            throw new BookingException("No tickets found");

        return tickets;
    }
}