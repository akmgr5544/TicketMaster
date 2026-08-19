using Events.Application.Commands;
using Events.Application.Exceptions;
using Events.Application.IntegrationEvents;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using MediatR;

namespace Events.Application.CommandHandlers;

internal sealed class CancelEventCommandHandler : IRequestHandler<CancelEventCommand>
{
    private readonly IEventRepository _repository;
    private readonly IIntegrationEventPublisher _publisher;

    public CancelEventCommandHandler(IEventRepository repository, IIntegrationEventPublisher publisher)
    {
        _repository = repository;
        _publisher = publisher;
    }

    /// <summary>
    /// Writes unconditionally, even when the aggregate treated the cancellation as a no-op. The
    /// write is idempotent and one wasted RU on a repeat request is cheaper than branching on
    /// whether the aggregate changed; the aggregate raising no event is what keeps the second call
    /// from announcing anything.
    /// </summary>
    public async Task Handle(CancelEventCommand request, CancellationToken cancellationToken)
    {
        var @event = await _repository.GetEventByIdAsync(request.Id, cancellationToken)
                     ?? throw new NotFoundException(nameof(Event), request.Id);

        @event.Cancel();

        await _repository.UpdateEventAsync(@event, cancellationToken);
        await _publisher.PublishPendingAsync(@event, cancellationToken);
    }
}
