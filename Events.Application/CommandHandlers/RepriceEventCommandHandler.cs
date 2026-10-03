using Events.Application.Commands;
using Events.Application.Exceptions;
using Events.Application.IntegrationEvents;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using Events.Domain.ValueObjects;
using MediatR;

namespace Events.Application.CommandHandlers;

internal sealed class RepriceEventCommandHandler : IRequestHandler<RepriceEventCommand>
{
    private readonly IEventRepository _repository;
    private readonly IIntegrationEventPublisher _publisher;

    public RepriceEventCommandHandler(IEventRepository repository, IIntegrationEventPublisher publisher)
    {
        _repository = repository;
        _publisher = publisher;
    }

    public async Task Handle(RepriceEventCommand request, CancellationToken cancellationToken)
    {
        var @event = await _repository.GetEventByIdAsync(request.Id, cancellationToken)
                     ?? throw new NotFoundException(nameof(Event), request.Id);

        if (request.Caller?.MayChange(@event) != true)
            throw new ForbiddenException(nameof(Event), request.Id);

        @event.Reprice(new TicketPrice(request.TicketPrice, request.Currency),
            (request.PriceTiers ?? []).Select(tier => new PriceTier(tier.Name, tier.Price, tier.Seats)));

        await _repository.UpdateEventAsync(@event, cancellationToken);
        await _publisher.PublishPendingAsync(@event, cancellationToken);
    }
}
