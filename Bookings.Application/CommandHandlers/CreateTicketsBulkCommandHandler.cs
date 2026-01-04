using Bookings.Application.Commands;
using Bookings.Domain.Entities;
using Bookings.Domain.Repositories;
using MediatR;

namespace Bookings.Application.CommandHandlers;

public class CreateTicketsBulkCommandHandler : IRequestHandler<CreateTicketsBulkCommand>
{
    private readonly ITicketsRepository _ticketsRepository;
    
    public CreateTicketsBulkCommandHandler(ITicketsRepository ticketsRepository)
    {
        _ticketsRepository = ticketsRepository;
    }
    
    public async Task Handle(CreateTicketsBulkCommand request, CancellationToken cancellationToken)
    {
        var tickets = new List<Ticket>();
        
        foreach (var seat in request.Seats)
        {
            var ticket = new Ticket(seat, request.VenueId, request.EventId, request.EventDate);
            tickets.Add(ticket);
        }
        
        await _ticketsRepository.AddTicketsAsync(tickets.ToArray());
        await _ticketsRepository.SaveChangesAsync(cancellationToken);
    }
}