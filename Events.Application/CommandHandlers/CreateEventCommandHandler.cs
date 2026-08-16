using Events.Application.Commands;
using Events.Domain.Exceptions;
using Events.Domain.Repositories;
using MediatR;
using TicketMaster.Common.IntegrationEvents;
using Wolverine;
using Event = Events.Domain.Entities.Event;

namespace Events.Application.CommandHandlers;

internal class CreateEventCommandHandler : IRequestHandler<CreateEventCommand>
{
    private readonly IEventRepository _eventRepository;
    private readonly IVenueRepository _venueRepository;
    private readonly IPerformerRepository _performerRepository;
    private readonly IMessageBus _messageBus;

    public CreateEventCommandHandler(IEventRepository eventRepository,
        IVenueRepository venueRepository,
        IPerformerRepository performerRepository,
        IMessageBus messageBus)
    {
        _eventRepository = eventRepository;
        _venueRepository = venueRepository;
        _performerRepository = performerRepository;
        _messageBus = messageBus;
    }

    public async Task Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        var performers = await _performerRepository.GetPerformersByIdsAsync(request.Performers, cancellationToken);
        if (performers.Count == 0)
            throw new EventsDomainException("No performers found");

        var venue = await _venueRepository.GetVenueByIdAsync(request.Venue, cancellationToken);
        if (venue is null)
            throw new EventsDomainException("No venue found");

        // The start-date rule lives in the Event constructor, not here — it is an invariant of the
        // aggregate rather than a check this particular caller happens to perform.
        var @event = new Event(request.StartDate, venue, performers);

        await _eventRepository.AddEventAsync(@event, cancellationToken);

        await _messageBus.PublishAsync(new EventCreatedIntegrationEvent(@event.Id,
            venue.Id,
            @event.StartDate,
            [..venue.Seats]));
    }
}
