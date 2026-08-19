using Events.Application.Commands;
using Events.Application.Exceptions;
using Events.Application.IntegrationEvents;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using MediatR;

namespace Events.Application.CommandHandlers;

internal sealed class RelocateEventCommandHandler : IRequestHandler<RelocateEventCommand>
{
    private readonly IEventRepository _eventRepository;
    private readonly IVenueRepository _venueRepository;
    private readonly IIntegrationEventPublisher _publisher;

    public RelocateEventCommandHandler(IEventRepository eventRepository,
        IVenueRepository venueRepository,
        IIntegrationEventPublisher publisher)
    {
        _eventRepository = eventRepository;
        _venueRepository = venueRepository;
        _publisher = publisher;
    }

    /// <summary>
    /// The venue is read from the venues container and embedded as a fresh snapshot. The seats that
    /// come with it are what downstream reconciles against, which is why the destination must exist
    /// before the event is touched.
    /// </summary>
    public async Task Handle(RelocateEventCommand request, CancellationToken cancellationToken)
    {
        var @event = await _eventRepository.GetEventByIdAsync(request.Id, cancellationToken)
                     ?? throw new NotFoundException(nameof(Event), request.Id);

        var venue = await _venueRepository.GetVenueByIdAsync(request.VenueId, cancellationToken)
                    ?? throw new NotFoundException(nameof(Venue), request.VenueId);

        @event.Relocate(venue);

        await _eventRepository.UpdateEventAsync(@event, cancellationToken);
        await _publisher.PublishPendingAsync(@event, cancellationToken);
    }
}
