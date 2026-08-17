using Events.Application.Dtos;
using Events.Application.Queries;
using Events.Domain.Repositories;
using MediatR;

namespace Events.Application.QueryHandlers;

internal sealed class ListVenuesQueryHandler : IRequestHandler<ListVenuesQuery, PagedResult<VenueDto>>
{
    private readonly IVenueRepository _repository;

    public ListVenuesQueryHandler(IVenueRepository repository)
    {
        _repository = repository;
    }

    public async Task<PagedResult<VenueDto>> Handle(ListVenuesQuery request, CancellationToken cancellationToken)
    {
        var page = await _repository.ListVenuesAsync(request.PageSize, request.ContinuationToken, cancellationToken);

        return new PagedResult<VenueDto>([..page.Items.Select(VenueDto.From)], page.ContinuationToken);
    }
}
