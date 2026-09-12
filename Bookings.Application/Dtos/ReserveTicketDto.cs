namespace Bookings.Application.Dtos;

public record ReserveTicketDto(long TicketId, string EventId, Guid UserId);