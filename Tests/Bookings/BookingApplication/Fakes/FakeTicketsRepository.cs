using System.Collections.Immutable;
using Bookings.Domain.Entities;
using Bookings.Domain.Repositories;

namespace BookingApplication.Fakes;

/// <summary>
/// A real in-memory implementation rather than a mock, so the tests assert on what actually happened
/// to the tickets instead of on which methods were called.
/// </summary>
internal sealed class FakeTicketsRepository : ITicketsRepository
{
    private readonly List<Ticket> _tickets = [];

    public int SaveCount { get; private set; }

    public IReadOnlyList<Ticket> Tickets => _tickets;

    public void Seed(params Ticket[] tickets) => _tickets.AddRange(tickets);

    public ValueTask<Ticket[]> GetTicketsByEventAsync(string eventId, CancellationToken cancellationToken) =>
        ValueTask.FromResult(_tickets.Where(t => t.EventId == eventId).ToArray());

    /// <summary>
    /// Mirrors the real query by going through <c>IsAvailableFor</c> — the same rule the production
    /// predicate expresses in SQL. A fake that only matched on id would be more permissive than the
    /// database and would let a test pass on a seat that is actually sold.
    /// </summary>
    public ValueTask<Ticket[]> GetTicketsForBookingAsync(ImmutableArray<long> ticketIds,
        string eventId,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(_tickets
            .Where(t => ticketIds.Contains(t.Id) && t.IsAvailableFor(eventId, DateTime.UtcNow))
            .ToArray());

    public ValueTask<Ticket[]> GetTicketsByIdAsync(ImmutableArray<long> ticketIds,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(_tickets.Where(t => ticketIds.Contains(t.Id)).ToArray());

    public ValueTask<Ticket[]> GetTicketsForReservationAsync(ImmutableArray<long> ticketIds,
        CancellationToken cancellationToken) =>
        ValueTask.FromResult(_tickets.Where(t => ticketIds.Contains(t.Id)).ToArray());

    public ValueTask AddTicketsAsync(Ticket[] ticket)
    {
        _tickets.AddRange(ticket);
        return ValueTask.CompletedTask;
    }

    public ValueTask AddTicketAsync(Ticket ticket, CancellationToken cancellationToken)
    {
        _tickets.Add(ticket);
        return ValueTask.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        SaveCount++;
        return Task.CompletedTask;
    }
}
