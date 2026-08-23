using Bookings.Application.Commands;
using Bookings.Application.Exceptions;
using Bookings.Application.Services.Interfaces;
using Bookings.Domain.Entities;
using Bookings.Domain.Exceptions;
using Bookings.Domain.Repositories;
using MediatR;
using Bookings.Application.Commands.Tickets;

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
        var @event = await _eventsService.GetEventByIdAsync(request.EventId, cancellationToken);
        
        if (@event == null)
            throw new NotFoundException("Event", request.EventId);

        if (@event.Venue.Id != request.VenueId)
            throw new BookingsDomainException("Wrong venue");
        
        if(!@event.Venue.Seats.Contains(request.Seat))
            throw new BookingsDomainException("Wrong seat");
        
        var ticket = new Ticket(request.Seat,
            request.VenueId,
            request.EventId,
            request.EventDate);
        
        await _ticketsRepository.AddTicketAsync(ticket, cancellationToken);
        await _ticketsRepository.SaveChangesAsync(cancellationToken);
    }
}