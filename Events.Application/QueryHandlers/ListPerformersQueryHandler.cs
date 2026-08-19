using Events.Application.Dtos;
using Events.Application.Queries;
using Events.Domain.Repositories;
using MediatR;

namespace Events.Application.QueryHandlers;

internal sealed class ListPerformersQueryHandler : IRequestHandler<ListPerformersQuery, PagedResult<PerformerDto>>
{
    private readonly IPerformerRepository _repository;

    public ListPerformersQueryHandler(IPerformerRepository repository)
    {
        _repository = repository;
    }

    public async Task<PagedResult<PerformerDto>> Handle(ListPerformersQuery request,
        CancellationToken cancellationToken)
    {
        var page = await _repository.ListPerformersAsync(request.PageSize,
            request.ContinuationToken,
            cancellationToken);

        return new PagedResult<PerformerDto>([..page.Items.Select(PerformerDto.From)], page.ContinuationToken);
    }
}
