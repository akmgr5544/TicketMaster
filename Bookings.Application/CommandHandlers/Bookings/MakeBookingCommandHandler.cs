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
using TicketMaster.Common.IntegrationEvents;

namespace Bookings.Application.CommandHandlers.Bookings;

internal sealed class MakeBookingCommandHandler : IRequestHandler<MakeBookingCommand, long>
{
    private readonly IBookingRepository _bookingRepository;
    private readonly ITicketsRepository _ticketsRepository;
    private readonly ICacheService _cacheService;
    private readonly IAfterCommitQueue _afterCommit;
    private readonly IIntegrationEventPublisher _integrationEvents;
    private const int TicketCountConfig = 2;

    public MakeBookingCommandHandler(IBookingRepository bookingRepository,
        ITicketsRepository ticketsRepository,
        ICacheService cacheService,
        IAfterCommitQueue afterCommit,
        IIntegrationEventPublisher integrationEvents)
    {
        _bookingRepository = bookingRepository;
        _ticketsRepository = ticketsRepository;
        _cacheService = cacheService;
        _afterCommit = afterCommit;
        _integrationEvents = integrationEvents;
    }

    public async Task<long> Handle(MakeBookingCommand request, CancellationToken cancellationToken)
    {
        var tickets = await GetValidTicketsAsync(request.Tickets,
            request.EventId,
            request.UserId,
            cancellationToken);
        var ticketIds = tickets.Select(ticket => ticket.Id).ToArray();
        var (sellerId, amount, currency) = PriceOf(tickets);

        var booking = Booking.Create(request.UserId, BookingStatus.Booked, ticketIds);

        _bookingRepository.Add(booking);
        await _bookingRepository.SaveChangesAsync(cancellationToken);

        // After the save, not from BookingCreatedDomainEvent: the id is assigned by the database, and the
        // domain event is raised before it exists.
        await _integrationEvents.PublishAsync(new PaymentRequestedIntegrationEvent(booking.Id,
                request.UserId,
                sellerId,
                amount,
                currency),
            cancellationToken);

        var reservationKeys = ticketIds.Select(ReservationKeys.Reservation).ToArray();
        _afterCommit.Enqueue(_ => _cacheService.RemoveAsync(reservationKeys));

        // Populated by the database during the save above.
        return booking.Id;
    }

    // One payment has one seller and one currency. Tickets for one event share both, so a mix means the
    // replica is inconsistent, and charging it as one payment would pay somebody the wrong money.
    private static (Guid SellerId, decimal Amount, string Currency) PriceOf(Ticket[] tickets)
    {
        var pricings = tickets.Select(ticket => ticket.Pricing!).ToArray();

        if (pricings.Select(p => (p.SellerId, p.Currency)).Distinct().Count() > 1)
            throw new BookingsApplicationException("The selected tickets are sold by different sellers or in different currencies");

        return (pricings[0].SellerId, pricings.Sum(p => p.Price), pricings[0].Currency);
    }

    private async Task<Ticket[]> GetValidTicketsAsync(long[] ticketIds,
        string eventId,
        Guid userId,
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

        return tickets;
    }
}