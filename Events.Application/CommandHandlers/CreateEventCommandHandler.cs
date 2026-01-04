using Events.Application.Commands;
using Events.Domain.Exceptions;
using Events.Domain.Repositories;
using MediatR;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;
using Event = Events.Domain.Entities.Event;

namespace Events.Application.CommandHandlers;

public class CreateEventCommandHandler : IRequestHandler<CreateEventCommand>
{
    private readonly IEventRepository _eventRepository;
    private readonly IMessageBus _messageBus;
    
    public CreateEventCommandHandler(IEventRepository eventRepository, IMessageBus messageBus)
    {
        _eventRepository = eventRepository;
        _messageBus = messageBus;
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
        await _messageBus.PublishAsync(new EventCreatedIntegrationEvent(@event.Id.ToString(),
            venue.Id.ToString(),
            @event.StartDate,
            venue.Seats
            ));
    }
}