using MediatR;

namespace Bookings.Application.Commands.Tickets;

// Deliberately not ITransactionalRequest: the handler calls Events before writing, and an open
// Postgres transaction must not span a network call. Its single SaveChangesAsync is atomic on its
// own, and creating a ticket raises no domain event for a transaction to hold together.
public record CreateTicketCommand(string EventId,
    string VenueId,
    string Seat,
    DateTime EventDate): IRequest;
