using Bookings.Domain.Abstractions;
using Bookings.Domain.Entities;
using MediatR;

namespace Bookings.Application.Commands;

public record RescheduleEventTicketsCommand(string EventId, long Version, DateTime StartDate)
    : IRequest, ITransactionalRequest;

public record CancelEventTicketsCommand(string EventId, long Version) : IRequest, ITransactionalRequest;

public record ReconcileEventVenueCommand(string EventId,
    long Version,
    string VenueId,
    DateTime StartDate,
    string[] Seats,
    TicketPricing? Pricing = null) : IRequest, ITransactionalRequest;
