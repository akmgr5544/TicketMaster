using Events.Application.Commands;
using Events.Application.Exceptions;
using Events.Application.IntegrationEvents;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using MediatR;

namespace Events.Application.CommandHandlers;

internal sealed class RescheduleEventCommandHandler : IRequestHandler<RescheduleEventCommand>
{
    private readonly IEventRepository _repository;
    private readonly IIntegrationEventPublisher _publisher;

    public RescheduleEventCommandHandler(IEventRepository repository, IIntegrationEventPublisher publisher)
    {
        _repository = repository;
        _publisher = publisher;
    }

    /// <summary>
    /// Load, mutate, write, then publish. The order matters in both directions: a refused mutation
    /// throws before the write so nothing is stored and nothing is announced, and publishing after
    /// the write means no consumer ever hears about a change that failed to persist.
    /// </summary>
    public async Task Handle(RescheduleEventCommand request, CancellationToken cancellationToken)
    {
        var @event = await _repository.GetEventByIdAsync(request.Id, cancellationToken)
                     ?? throw new NotFoundException(nameof(Event), request.Id);

        @event.Reschedule(request.StartDate);

        await _repository.UpdateEventAsync(@event, cancellationToken);
        await _publisher.PublishPendingAsync(@event, cancellationToken);
    }
}
