using MediatR;

namespace Bookings.Application.Commands;

public record CreateTicketCommand(string EventId,
    string VenueId,
    string Seat,
    DateTime EventDate): IRequest;