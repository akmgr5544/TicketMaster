using Events.Application.Dtos;
using Events.Application.Queries;
using Events.Domain.Repositories;
using MediatR;

namespace Events.Application.QueryHandlers;

internal sealed class ListEventsQueryHandler : IRequestHandler<ListEventsQuery, PagedResult<EventDto>>
{
    private readonly IEventRepository _repository;

    public ListEventsQueryHandler(IEventRepository repository)
    {
        _repository = repository;
    }

    public async Task<PagedResult<EventDto>> Handle(ListEventsQuery request, CancellationToken cancellationToken)
    {
        var page = await _repository.ListEventsAsync(request.PageSize, request.ContinuationToken, cancellationToken);

        return new PagedResult<EventDto>([..page.Items.Select(EventDto.From)], page.ContinuationToken);
    }
}
