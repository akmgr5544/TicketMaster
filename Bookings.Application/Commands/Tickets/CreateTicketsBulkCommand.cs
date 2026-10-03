using Bookings.Domain.Abstractions;
using MediatR;
using TicketMaster.Common.IntegrationEvents;

namespace Bookings.Application.Commands.Tickets;

public record CreateTicketsBulkCommand(
    string EventId,
    string VenueId,
    DateTime EventDate,
    string[] Seats,
    long Version,
    EventPricing? Pricing = null):IRequest, ITransactionalRequest;