using Bookings.Application.Dtos.EventsServiceDtos;

namespace Bookings.Application.Services.Interfaces;

public interface IEventsService
{
    /// <summary>Null when the catalogue has no such event.</summary>
    Task<EventDto?> GetEventByIdAsync(string id, CancellationToken cancellationToken);
}
