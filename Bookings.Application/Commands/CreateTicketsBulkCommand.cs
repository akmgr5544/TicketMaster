using MediatR;

namespace Bookings.Application.Commands;

public record CreateTicketsBulkCommand(string EventId,
    string VenueId,
    DateTime EventDate,
    string[] Seats):IRequest;