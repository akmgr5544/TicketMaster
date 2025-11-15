using Events.Application.Commands;
using Events.Mongo;
using MediatR;

namespace Events.Application.CommandHandlers;

public class AddPerformerCommandHandler : IRequestHandler<AddPerformerCommand>
{
    private readonly MongoDomainContext _context;
    
    public AddPerformerCommandHandler(MongoDomainContext context)
    {
        _context = context;
    }
    
    public Task Handle(AddPerformerCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}