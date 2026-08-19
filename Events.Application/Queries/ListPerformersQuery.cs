using Events.Application.Dtos;
using MediatR;

namespace Events.Application.Queries;

public record ListPerformersQuery(int PageSize, string? ContinuationToken)
    : IRequest<PagedResult<PerformerDto>>;
