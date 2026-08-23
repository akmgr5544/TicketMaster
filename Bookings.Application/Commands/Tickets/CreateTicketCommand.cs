using Bookings.Domain.Abstractions;
using MediatR;

namespace Bookings.Application.Commands.Tickets;

public record CreateTicketCommand(string EventId,
    string VenueId,
    string Seat,
    DateTime EventDate): IRequest, ITransactionalRequest;