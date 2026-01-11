using Bookings.Application.Dtos.EventsServiceDtos;

namespace Bookings.Application.Services.Interfaces;

public interface IEventsService
{
    Task<EventDto> GetEventByIdAsync(string id, CancellationToken cancellationToken);
}