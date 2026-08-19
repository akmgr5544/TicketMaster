using Events.Application.Dtos;
using MediatR;

namespace Events.Application.Queries;

public record ListEventsQuery(int PageSize, string? ContinuationToken)
    : IRequest<PagedResult<EventDto>>;
