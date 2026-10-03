using Bookings.Domain.Abstractions;
using Bookings.Domain.Entities;
using MediatR;

namespace Bookings.Application.Commands.Tickets;

public record CreateTicketsBulkCommand(
    string EventId,
    string VenueId,
    DateTime EventDate,
    string[] Seats,
    long Version,
    TicketPricing? Pricing = null):IRequest, ITransactionalRequest;