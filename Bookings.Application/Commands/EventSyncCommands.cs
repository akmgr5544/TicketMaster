using Bookings.Domain.Abstractions;
using MediatR;
using TicketMaster.Common.IntegrationEvents;

namespace Bookings.Application.Commands;

public record RescheduleEventTicketsCommand(string EventId, long Version, DateTime StartDate)
    : IRequest, ITransactionalRequest;

public record CancelEventTicketsCommand(string EventId, long Version) : IRequest, ITransactionalRequest;

public record ReconcileEventVenueCommand(string EventId,
    long Version,
    string VenueId,
    DateTime StartDate,
    string[] Seats,
    EventPricing? Pricing = null) : IRequest, ITransactionalRequest;

public record RepriceEventTicketsCommand(string EventId, long Version, EventPricing Pricing)
    : IRequest, ITransactionalRequest;
