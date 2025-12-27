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

    ValueTask AddTicketsAsync(Ticket[] ticket);

    ValueTask AddTicketAsync(Ticket ticket, CancellationToken cancellationToken);
}