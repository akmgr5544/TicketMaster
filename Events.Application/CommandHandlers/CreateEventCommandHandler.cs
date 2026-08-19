using Events.Application.Commands;
using Events.Application.IntegrationEvents;
using Events.Domain.Exceptions;
using Events.Domain.Repositories;
using MediatR;
using Event = Events.Domain.Entities.Event;

namespace Events.Application.CommandHandlers;

internal sealed class CreateEventCommandHandler : IRequestHandler<CreateEventCommand, string>
{
    private readonly IEventRepository _eventRepository;
    private readonly IVenueRepository _venueRepository;
    private readonly IPerformerRepository _performerRepository;
    private readonly IIntegrationEventPublisher _publisher;

    public CreateEventCommandHandler(IEventRepository eventRepository,
        IVenueRepository venueRepository,
        IPerformerRepository performerRepository,
        IIntegrationEventPublisher publisher)
    {
        _eventRepository = eventRepository;
        _venueRepository = venueRepository;
        _performerRepository = performerRepository;
        _publisher = publisher;
    }

    public async Task<string> Handle(CreateEventCommand request, CancellationToken cancellationToken)
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

        // The aggregate raised EventCreated in its constructor, so creation now travels the same
        // path as every other change instead of building the contract by hand here.
        await _publisher.PublishPendingAsync(@event, cancellationToken);

        return @event.Id;
    }
}
