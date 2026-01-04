using Events.Application.Commands;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using Events.Mongo;
using MediatR;

namespace Events.Application.CommandHandlers;

public class AddVenueCommandHandler : IRequestHandler<AddVenueCommand>
{
    private readonly IVenueRepository _repository;
    
    public AddVenueCommandHandler(IVenueRepository repository)
    {
        _repository = repository;
    }
    
    public async Task Handle(AddVenueCommand request, CancellationToken cancellationToken)
    {
        var venue = new Venue(request.Name,
            request.Address,
            request.Location,
            request.Seats);
        
        await _repository.AddVenueAsync(venue, cancellationToken: cancellationToken);
    }
}