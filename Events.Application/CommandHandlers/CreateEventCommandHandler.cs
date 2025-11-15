using Events.Application.Commands;
using Events.Mongo;
using MediatR;

namespace Events.Application.CommandHandlers;

public class CreateEventCommandHandler : IRequestHandler<CreateEventCommand>
{
    private readonly MongoDomainContext _context;
    
    public CreateEventCommandHandler(MongoDomainContext context)
    {
        _context = context;
    }
    
    public Task Handle(CreateEventCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}