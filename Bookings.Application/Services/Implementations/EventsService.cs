using Bookings.Application.Dtos.EventsServiceDtos;
using Bookings.Application.Services.Interfaces;

namespace Bookings.Application.Services.Implementations;

public class EventsService : IEventsService
{
    public Task<EventDto> GetEventByIdAsync(string id, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}