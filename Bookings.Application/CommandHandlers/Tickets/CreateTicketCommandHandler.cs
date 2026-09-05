using Bookings.Application.Commands.Tickets;
using Bookings.Application.Exceptions;
using Bookings.Application.Services.Interfaces;
using Bookings.Domain.Entities;
using Bookings.Domain.Exceptions;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers.Tickets;

internal class CreateTicketCommandHandler : IRequestHandler<CreateTicketCommand>
{
    private readonly ITicketsRepository _ticketsRepository;
    private readonly IEventsService _eventsService;

    public CreateTicketCommandHandler(ITicketsRepository ticketsRepository,
        IEventsService eventsService)
    {
        _ticketsRepository = ticketsRepository;
        _eventsService = eventsService;
    }

    public async Task Handle(CreateTicketCommand request, CancellationToken cancellationToken)
    {
        // Asked of Events rather than of Bookings' own tickets: this command is the admin's repair
        // tool, so the local replica is the thing it is being used to fix. See the `rpc` skill.
        var @event = await _eventsService.GetEventByIdAsync(request.EventId, cancellationToken);

        if (@event is null)
            throw new NotFoundException("Event", request.EventId);

        if (@event.Venue.Id != request.VenueId)
            throw new BookingsDomainException("Wrong venue");

        if (!@event.Venue.Seats.Contains(request.Seat))
            throw new BookingsDomainException("Wrong seat");

        // Events knows the seat is real; only Bookings knows whether it has already sold it.
        if (await _ticketsRepository.SeatIsCoveredAsync(request.EventId, request.Seat, cancellationToken))
            throw new BookingsDomainException(
                $"Seat {request.Seat} already has a ticket for event {request.EventId}.");

        var ticket = new Ticket(request.Seat,
            request.VenueId,
            request.EventId,
            request.EventDate,
            await _ticketsRepository.GetAppliedVersionForEventAsync(request.EventId, cancellationToken));

        await _ticketsRepository.AddTicketAsync(ticket, cancellationToken);
        await _ticketsRepository.SaveChangesAsync(cancellationToken);
    }
}
