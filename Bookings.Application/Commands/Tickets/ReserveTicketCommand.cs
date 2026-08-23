using MediatR;

namespace Bookings.Application.Commands.Tickets;

public record ReserveTicketCommand(
    string UserId,
    string EventId,
    long[] Tickets) : IRequest;