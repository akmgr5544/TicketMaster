using Events.Application.Commands;
using Events.Application.Exceptions;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using MediatR;

namespace Events.Application.CommandHandlers;

internal sealed class DeleteVenueCommandHandler : IRequestHandler<DeleteVenueCommand>
{
    private readonly IVenueRepository _venueRepository;
    private readonly IEventRepository _eventRepository;

    public DeleteVenueCommandHandler(IVenueRepository venueRepository, IEventRepository eventRepository)
    {
        _venueRepository = venueRepository;
        _eventRepository = eventRepository;
    }

    public async Task Handle(DeleteVenueCommand request, CancellationToken cancellationToken)
    {
        var venue = await _venueRepository.GetVenueByIdAsync(request.Id, cancellationToken)
                    ?? throw new NotFoundException(nameof(Venue), request.Id);

        // Best-effort guard, not a guarantee: an event can be created between this count and the
        // delete below. With /id partition keys the two documents never share a logical partition,
        // so no transaction can close that window. It stops the accident, not the race.
        var upcoming = await _eventRepository.CountUpcomingEventsAtVenueAsync(venue.Id,
            DateTime.UtcNow,
            cancellationToken);

        if (upcoming > 0)
            throw new EventsApplicationException(
                $"Venue '{venue.Id}' cannot be deleted because it has {upcoming} upcoming event(s)");

        await _venueRepository.DeleteVenueAsync(venue.Id, cancellationToken);
    }
}
