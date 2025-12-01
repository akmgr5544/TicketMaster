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
    public Task Handle(CreateTicketCommand request, CancellationToken cancellationToken)
    {
        //TODO:: add bulk insertion for Tickets on EventCreated and pass total seats for venue
        var ticket = new Ticket(request.Seat,
            request.VenueId,
            request.EventId,
            request.EventDate);
        throw new NotImplementedException();
    }
}