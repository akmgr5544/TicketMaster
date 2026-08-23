using MediatR;

namespace Bookings.Application.Commands;

public record ReserveTicketCommand(
    string UserId,
    string EventId,
    long[] Tickets) : IRequest;