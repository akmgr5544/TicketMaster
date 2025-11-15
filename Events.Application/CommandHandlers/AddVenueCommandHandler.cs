using Events.Application.Commands;
using Events.Mongo;
using MediatR;

namespace Events.Application.CommandHandlers;

public class AddVenueCommandHandler : IRequestHandler<AddVenueCommand>
{
    private readonly MongoDomainContext _context;
    
    public AddVenueCommandHandler(MongoDomainContext context)
    {
        _context = context;
    }
    
    public Task Handle(AddVenueCommand request, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}