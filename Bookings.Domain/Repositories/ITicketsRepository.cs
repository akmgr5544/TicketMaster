using System.Collections.Immutable;
using Bookings.Domain.Abstractions;
using Bookings.Domain.Entities;

namespace Bookings.Domain.Repositories;

public interface ITicketsRepository : IUnitOfWork
{
    ValueTask<Ticket[]> GetTicketsForBookingAsync(ImmutableArray<long> ticketIds,
        string eventId,
        CancellationToken cancellationToken);
    ValueTask<Ticket[]> GetTicketsByIdAsync(ImmutableArray<long> ticketIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every ticket for one catalogue event, tracked, because callers are reconciling them against a
    /// change from the Events service. Bounded by the seats at a venue rather than by time, so this
    /// is deliberately unpaginated.
    /// </summary>
    ValueTask<Ticket[]> GetTicketsByEventAsync(string eventId, CancellationToken cancellationToken);

    /// <summary>
    /// The tickets with these ids, untracked, for deciding whether they can be reserved. By id alone
    /// rather than filtered by availability, so the caller can tell a ticket that does not exist from
    /// one that exists but is not for sale — and say which.
    /// </summary>
    ValueTask<Ticket[]> GetTicketsForReservationAsync(ImmutableArray<long> ticketIds,
        CancellationToken cancellationToken);

    ValueTask AddTicketsAsync(Ticket[] ticket);

    ValueTask AddTicketAsync(Ticket ticket, CancellationToken cancellationToken);
}