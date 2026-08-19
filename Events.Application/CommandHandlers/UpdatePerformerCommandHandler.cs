using Events.Application.Commands;
using Events.Application.Exceptions;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using MediatR;

namespace Events.Application.CommandHandlers;

internal sealed class UpdatePerformerCommandHandler : IRequestHandler<UpdatePerformerCommand>
{
    private readonly IPerformerRepository _repository;

    public UpdatePerformerCommandHandler(IPerformerRepository repository)
    {
        _repository = repository;
    }

    public async Task Handle(UpdatePerformerCommand request, CancellationToken cancellationToken)
    {
        var performer = await _repository.GetPerformerByIdAsync(request.Id, cancellationToken)
                        ?? throw new NotFoundException(nameof(Performer), request.Id);

        // Through the aggregate's own behaviour, so the same validation applies as at creation.
        // Both mutations happen before the write, so a rejected value persists nothing.
        performer.Rename(request.Name);
        performer.ChangeDescription(request.Description);

        await _repository.UpdatePerformerAsync(performer, cancellationToken);
    }
}
