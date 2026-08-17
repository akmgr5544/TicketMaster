using Events.Application.Commands;
using Events.Application.Exceptions;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using Events.Domain.ValueObjects;
using MediatR;

namespace Events.Application.CommandHandlers;

internal sealed class UpdateVenueCommandHandler : IRequestHandler<UpdateVenueCommand>
{
    private readonly IVenueRepository _repository;

    public UpdateVenueCommandHandler(IVenueRepository repository)
    {
        _repository = repository;
    }

    public async Task Handle(UpdateVenueCommand request, CancellationToken cancellationToken)
    {
        var venue = await _repository.GetVenueByIdAsync(request.Id, cancellationToken)
                    ?? throw new NotFoundException(nameof(Venue), request.Id);

        // Through the aggregate's own behaviour, so the same validation applies as at creation.
        // Both mutations happen before the write, so a rejected value persists nothing.
        venue.Rename(request.Name);
        venue.Relocate(new GeoLocation(request.Latitude, request.Longitude));
        venue.ChangeAddress(request.Address);

        await _repository.UpdateVenueAsync(venue, cancellationToken);
    }
}
