using Events.Application.Dtos;
using Events.Application.Exceptions;
using Events.Application.Queries;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using MediatR;

namespace Events.Application.QueryHandlers;

internal sealed class GetEventQueryHandler : IRequestHandler<GetEventQuery, EventDto>
{
    private readonly IEventRepository _repository;

    public GetEventQueryHandler(IEventRepository repository)
    {
        _repository = repository;
    }

    public async Task<EventDto> Handle(GetEventQuery request, CancellationToken cancellationToken)
    {
        var @event = await _repository.GetEventByIdAsync(request.Id, cancellationToken)
                     ?? throw new NotFoundException(nameof(Event), request.Id);

        return EventDto.From(@event);
    }
}
