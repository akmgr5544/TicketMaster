using Events.Application.Commands;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using MediatR;

namespace Events.Application.CommandHandlers;

internal sealed class AddPerformerCommandHandler : IRequestHandler<AddPerformerCommand, string>
{
    private readonly IPerformerRepository _repository;

    public AddPerformerCommandHandler(IPerformerRepository repository)
    {
        _repository = repository;
    }

    public async Task<string> Handle(AddPerformerCommand request, CancellationToken cancellationToken)
    {
        var performer = new Performer(request.Name, request.Description);

        await _repository.AddPerformerAsync(performer, cancellationToken);

        return performer.Id;
    }
}
