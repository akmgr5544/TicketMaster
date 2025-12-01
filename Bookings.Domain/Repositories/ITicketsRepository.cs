using System.Collections.Immutable;
using Bookings.Domain.Entities;

namespace Bookings.Domain.Repositories;

public interface ITicketsRepository
{
    ValueTask<Ticket[]> GetTicketsByIdAsync(ImmutableArray<long> ticketIds,
        CancellationToken cancellationToken);

    ValueTask AddTicketsAsync(Ticket[] ticket);
}