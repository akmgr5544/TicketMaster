using Events.Application.Commands;
using Events.Domain.Exceptions;
using Events.Domain.Repositories;
using MassTransit;
using MediatR;
using Event = Events.Domain.Entities.Event;

namespace Events.Application.CommandHandlers;

public class CreateEventCommandHandler : IRequestHandler<CreateEventCommand>
{
    private readonly IEventRepository _eventRepository;
    
    public CreateEventCommandHandler(IEventRepository eventRepository, IBus bus)
    {
        _eventRepository = eventRepository;
    }
    
    public async Task Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        if (request.StartDate < DateTime.UtcNow.AddDays(10))
            throw new EventsDomainException("Start date must be in the future");
        
        var performers = await _eventRepository.GetPerformersByIdsAsync(request.Performers, cancellationToken);
        if (performers.Count == 0)
            throw new EventsDomainException("No performers found");
        
        var venue = await _eventRepository.GetVenueByIdAsync(request.Venue, cancellationToken);
        if (venue == null)
            throw new EventsDomainException("No venue found");

        var @event = new Event(request.StartDate,
            venue,
            performers);
        
        await _eventRepository.AddEventAsync(@event, cancellationToken);
        //TODO:: publish integration event
    }
}