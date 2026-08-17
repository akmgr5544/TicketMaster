using Events.Application.Dtos;
using MediatR;

namespace Events.Application.Queries;

public record ListVenuesQuery(int PageSize, string? ContinuationToken)
    : IRequest<PagedResult<VenueDto>>;
