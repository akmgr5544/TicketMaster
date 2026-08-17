using Events.Application.Dtos;
using Events.Application.Queries;
using Events.Application.Exceptions;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using MediatR;

namespace Events.Application.QueryHandlers;

internal sealed class GetVenueQueryHandler : IRequestHandler<GetVenueQuery, VenueDto>
{
    private readonly IVenueRepository _repository;

    public GetVenueQueryHandler(IVenueRepository repository)
    {
        _repository = repository;
    }

    public async Task<VenueDto> Handle(GetVenueQuery request, CancellationToken cancellationToken)
    {
        var venue = await _repository.GetVenueByIdAsync(request.Id, cancellationToken)
                    ?? throw new NotFoundException(nameof(Venue), request.Id);

        return VenueDto.From(venue);
    }
}
