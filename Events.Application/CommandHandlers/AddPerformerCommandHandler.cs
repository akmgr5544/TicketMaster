using Events.Application.Commands;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using Events.Mongo;
using MediatR;

namespace Events.Application.CommandHandlers;

public class AddPerformerCommandHandler : IRequestHandler<AddPerformerCommand>
{
    private readonly IPerformerRepository _repository;
    
    public AddPerformerCommandHandler(IPerformerRepository repository)
    {
        _repository = repository;
    }
    
    public async Task Handle(AddPerformerCommand request, CancellationToken cancellationToken)
    {
        var performer = new Performer(request.Name, request.Description);
        
        await _repository.AddPerformerAsync(performer, cancellationToken);
    }
}