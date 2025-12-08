using Bookings.Application.Commands;
using Bookings.Domain.Entities;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers;

internal class CreateTicketCommandHandler : IRequestHandler<CreateTicketCommand>
{
    private readonly ITicketsRepository _ticketsRepository;
    public CreateTicketCommandHandler(ITicketsRepository ticketsRepository)
    {
        _ticketsRepository = ticketsRepository;
    }
    public async Task Handle(CreateTicketCommand request, CancellationToken cancellationToken)
    {
        //TODO:: add validation for Venues and Events
        var ticket = new Ticket(request.Seat,
            request.VenueId,
            request.EventId,
            request.EventDate);
        
        await _ticketsRepository.AddTicketAsync(ticket, cancellationToken);
        await _ticketsRepository.SaveChangesAsync(cancellationToken);
    }
}