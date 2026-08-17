using Events.Application.Commands;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using Events.Domain.ValueObjects;
using MediatR;

namespace Events.Application.CommandHandlers;

internal sealed class AddVenueCommandHandler : IRequestHandler<AddVenueCommand, string>
{
    private readonly IVenueRepository _repository;

    public AddVenueCommandHandler(IVenueRepository repository)
    {
        _repository = repository;
    }

    public async Task<string> Handle(AddVenueCommand request, CancellationToken cancellationToken)
    {
        var venue = new Venue(request.Name,
            request.Address,
            new GeoLocation(request.Latitude, request.Longitude),
            request.Seats);

        await _repository.AddVenueAsync(venue, cancellationToken);

        return venue.Id;
    }
}
