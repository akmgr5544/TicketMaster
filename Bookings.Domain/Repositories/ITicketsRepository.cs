using System.Collections.Immutable;
using Bookings.Domain.Abstractions;
using Bookings.Domain.Entities;

namespace Bookings.Domain.Repositories;

public interface ITicketsRepository : IUnitOfWork
{
    ValueTask<Ticket[]> GetTicketsByIdAsync(ImmutableArray<long> ticketIds,
        CancellationToken cancellationToken);
    ValueTask<Ticket[]> GetTicketsByEventAsync(string eventId, CancellationToken cancellationToken);

    ValueTask<Ticket[]> GetTicketsForReservationAsync(ImmutableArray<long> ticketIds,
        CancellationToken cancellationToken);

    ValueTask<long> GetAppliedVersionForEventAsync(string eventId, CancellationToken cancellationToken);

    ValueTask<bool> SeatIsCoveredAsync(string eventId, string seat, CancellationToken cancellationToken);

    ValueTask AddTicketsAsync(Ticket[] ticket);

    ValueTask AddTicketAsync(Ticket ticket, CancellationToken cancellationToken);
}