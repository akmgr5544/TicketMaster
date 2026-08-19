using Events.Application.Commands;
using Events.Application.Exceptions;
using Events.Application.IntegrationEvents;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using MediatR;

namespace Events.Application.CommandHandlers;

internal sealed class ChangeEventLineupCommandHandler : IRequestHandler<ChangeEventLineupCommand>
{
    private readonly IEventRepository _eventRepository;
    private readonly IPerformerRepository _performerRepository;
    private readonly IIntegrationEventPublisher _publisher;

    public ChangeEventLineupCommandHandler(IEventRepository eventRepository,
        IPerformerRepository performerRepository,
        IIntegrationEventPublisher publisher)
    {
        _eventRepository = eventRepository;
        _performerRepository = performerRepository;
        _publisher = publisher;
    }

    public async Task Handle(ChangeEventLineupCommand request, CancellationToken cancellationToken)
    {
        var @event = await _eventRepository.GetEventByIdAsync(request.Id, cancellationToken)
                     ?? throw new NotFoundException(nameof(Event), request.Id);

        var requested = request.PerformerIds.Distinct().ToList();
        var performers = await _performerRepository.GetPerformersByIdsAsync(requested, cancellationToken);

        // An id that matched nothing is reported rather than silently dropped — otherwise the caller
        // asks for three performers, gets two, and is told the change succeeded.
        var missing = requested.Except(performers.Select(p => p.Id)).ToList();
        if (missing.Count > 0)
            throw new NotFoundException(nameof(Performer), string.Join(", ", missing));

        @event.ChangeLineup(performers);

        await _eventRepository.UpdateEventAsync(@event, cancellationToken);

        // Publishes nothing today: a lineup change has no integration contract. The call stays so
        // that adding one later needs no change here.
        await _publisher.PublishPendingAsync(@event, cancellationToken);
    }
}
