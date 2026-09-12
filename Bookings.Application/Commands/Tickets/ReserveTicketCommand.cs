using MediatR;

namespace Bookings.Application.Commands.Tickets;

public record ReserveTicketCommand(
    Guid UserId,
    string EventId,
    long[] Tickets) : IRequest;