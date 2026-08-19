using Events.Application.Dtos;
using Events.Application.Exceptions;
using Events.Application.Queries;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using MediatR;

namespace Events.Application.QueryHandlers;

internal sealed class GetPerformerQueryHandler : IRequestHandler<GetPerformerQuery, PerformerDto>
{
    private readonly IPerformerRepository _repository;

    public GetPerformerQueryHandler(IPerformerRepository repository)
    {
        _repository = repository;
    }

    public async Task<PerformerDto> Handle(GetPerformerQuery request, CancellationToken cancellationToken)
    {
        var performer = await _repository.GetPerformerByIdAsync(request.Id, cancellationToken)
                        ?? throw new NotFoundException(nameof(Performer), request.Id);

        return PerformerDto.From(performer);
    }
}
