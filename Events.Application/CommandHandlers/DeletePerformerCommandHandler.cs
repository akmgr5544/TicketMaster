using Events.Application.Commands;
using Events.Application.Exceptions;
using Events.Domain.Entities;
using Events.Domain.Repositories;
using MediatR;

namespace Events.Application.CommandHandlers;

internal sealed class DeletePerformerCommandHandler : IRequestHandler<DeletePerformerCommand>
{
    private readonly IPerformerRepository _performerRepository;
    private readonly IEventRepository _eventRepository;

    public DeletePerformerCommandHandler(IPerformerRepository performerRepository, IEventRepository eventRepository)
    {
        _performerRepository = performerRepository;
        _eventRepository = eventRepository;
    }

    public async Task Handle(DeletePerformerCommand request, CancellationToken cancellationToken)
    {
        var performer = await _performerRepository.GetPerformerByIdAsync(request.Id, cancellationToken)
                        ?? throw new NotFoundException(nameof(Performer), request.Id);

        // Best-effort guard on the same terms as venue deletion: an event can be created between
        // this count and the delete below, and with /id partition keys no transaction can close
        // that window. It stops the accident, not the race.
        var upcoming = await _eventRepository.CountUpcomingEventsWithPerformerAsync(performer.Id,
            DateTime.UtcNow,
            cancellationToken);

        if (upcoming > 0)
            throw new EventsApplicationException(
                $"Performer '{performer.Id}' cannot be deleted because they are booked for {upcoming} upcoming event(s)");

        await _performerRepository.DeletePerformerAsync(performer.Id, cancellationToken);
    }
}
