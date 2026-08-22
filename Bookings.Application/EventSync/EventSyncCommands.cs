using Bookings.Domain.Abstractions;
using MediatR;

namespace Bookings.Application.EventSync;

/// <summary>
/// Commands that bring tickets back in line with a change to the catalogue. Each carries the Events
/// aggregate's <c>Version</c> at the moment of the change; handlers use it to discard anything that
/// is not newer than what has already been applied.
/// <para>
/// Commands and their handlers share this namespace because <c>ColocationTest</c> requires it.
/// </para>
/// </summary>
public record RescheduleEventTicketsCommand(string EventId, long Version, DateTime StartDate)
    : IRequest, ITransactionalRequest;

public record CancelEventTicketsCommand(string EventId, long Version) : IRequest, ITransactionalRequest;

/// <summary>
/// <paramref name="Seats"/> is the full set the event now has, not the seats added or removed — the
/// handler works out the difference against what it holds.
/// </summary>
public record ReconcileEventVenueCommand(string EventId,
    long Version,
    string VenueId,
    DateTime StartDate,
    string[] Seats) : IRequest, ITransactionalRequest;
