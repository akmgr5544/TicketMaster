using Bookings.Application.Commands.Tickets;
using Bookings.Application.Dtos;
using Bookings.Application.Exceptions;
using Bookings.Application.Extensions;
using Bookings.Domain.Exceptions;
using Bookings.Sql;
using BookingIntegration.Fixtures;
using Medallion.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace BookingIntegration.Handlers;

/// <summary>
/// Reserving is what stops two people buying one seat, and it rests on a lock per ticket, taken in a
/// fixed order, with the check and the write both inside those locks. Run against real Postgres and
/// Redis so the lock and TTL assertions observe genuine contention rather than a fake's own bookkeeping.
/// </summary>
public sealed class ReserveTicketTests : IntegrationTest
{
    public ReserveTicketTests(BookingsFixture fixture) : base(fixture)
    {
    }

    // Cache and Redis come from IntegrationTest.

    // --- Reserving ---

    [Fact]
    public async Task Reserves_every_ticket_it_was_given()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        var ids = tickets.Select(t => t.Id).ToArray();

        await Sender.Send(new ReserveTicketCommand("user-1", "evt-1", ids));

        var held = await Cache.GetByKeysAsync<ReserveTicketDto>(
            ids.Select(ReservationKeys.Reservation).ToArray());

        Assert.Equal(2, held.Count);
    }

    [Fact]
    public async Task Records_who_reserved_the_ticket_and_for_which_event()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");

        await Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [tickets[0].Id]));

        var reservation = Assert.Single(
            await Cache.GetByKeysAsync<ReserveTicketDto>([ReservationKeys.Reservation(tickets[0].Id)]));
        Assert.Equal(new ReserveTicketDto(tickets[0].Id, "evt-1", "user-1"), reservation);
    }

    [Fact]
    public async Task Holds_the_reservation_for_the_configured_time()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");

        await Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [tickets[0].Id]));

        var ttl = await Redis.KeyTimeToLiveAsync(ReservationKeys.Reservation(tickets[0].Id));

        Assert.NotNull(ttl);
        // Five minutes, allowing for the round trip. Asserting a range rather than equality because the
        // clock moves between the SET and this read.
        Assert.InRange(ttl!.Value, TimeSpan.FromMinutes(4.5), TimeSpan.FromMinutes(5));
    }

    // --- Contention ---

    [Fact]
    public async Task Refuses_a_ticket_somebody_else_has_already_reserved()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        await Seed.ReservationAsync("other-user", "evt-1", tickets[0].Id);

        await Assert.ThrowsAsync<BookingsApplicationException>(() =>
            Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [tickets[0].Id])));
    }

    /// <summary>
    /// All or nothing: a request for two seats where one is taken reserves neither, and leaves the
    /// seat it could have had free for somebody else.
    /// </summary>
    [Fact]
    public async Task Reserves_nothing_when_one_of_the_tickets_is_taken()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        await Seed.ReservationAsync("other-user", "evt-1", tickets[1].Id);

        await Assert.ThrowsAsync<BookingsApplicationException>(() =>
            Sender.Send(new ReserveTicketCommand("user-1", "evt-1", tickets.Select(t => t.Id).ToArray())));

        var held = await Cache.GetByKeysAsync<ReserveTicketDto>(
            tickets.Select(t => ReservationKeys.Reservation(t.Id)).ToArray());
        Assert.Equal([new ReserveTicketDto(tickets[1].Id, "evt-1", "other-user")], held);
    }

    [Fact]
    public async Task Refuses_when_another_reservation_is_holding_the_ticket()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        var locks = Act.GetRequiredService<IDistributedLockProvider>();

        // Held by somebody else for the duration of the attempt.
        await using var held = await locks.AcquireLockAsync(ReservationKeys.Lock(tickets[0].Id));

        await Assert.ThrowsAsync<BookingsApplicationException>(() =>
            Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [tickets[0].Id])));
    }

    // --- Locking ---

    /// <summary>
    /// A constant lock key would serialize every reservation behind whichever one runs first. Holding a
    /// ticket in a different event and expecting an unrelated reservation to still go through is the
    /// only way to prove the keys are actually per-ticket.
    /// </summary>
    [Fact]
    public async Task Locks_each_ticket_rather_than_everything_at_once()
    {
        var elsewhere = await Seed.TicketsAsync("evt-2", "B1");
        var tickets = await Seed.TicketsAsync("evt-1", "A1");

        var locks = Act.GetRequiredService<IDistributedLockProvider>();
        await using var held = await locks.AcquireLockAsync(ReservationKeys.Lock(elsewhere[0].Id));

        await Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [tickets[0].Id]));

        var reservation = await Cache.GetByKeysAsync<ReserveTicketDto>([ReservationKeys.Reservation(tickets[0].Id)]);
        Assert.Single(reservation);
    }

    /// <summary>
    /// The deadlock guard, and the reason the order is not the caller's. Under the handler's real
    /// ascending sort, it blocks on the held lower id and never even attempts the higher one — so
    /// probing the higher id's lock while the send is still in flight finds it free. Under the
    /// caller's order it would grab the higher id first and hold it for the whole wait, and the same
    /// probe would find it taken. That difference only exists while the send is blocked: both orders
    /// end in the identical throw, empty cache and elapsed wait once the lock attempt times out, so
    /// asserting only the aftermath — as an earlier version of this test did — cannot tell them apart.
    /// </summary>
    [Fact]
    public async Task Takes_the_locks_in_ticket_order_not_the_order_it_was_asked_for()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        var ids = tickets.Select(t => t.Id).OrderBy(id => id).ToArray();
        var (lowerId, higherId) = (ids[0], ids[1]);

        var locks = Act.GetRequiredService<IDistributedLockProvider>();
        await using var held = await locks.AcquireLockAsync(ReservationKeys.Lock(lowerId));

        // Not awaited yet: the send has to still be blocked on the lower id's lock when we probe.
        var send = Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [higherId, lowerId]));

        // Poll rather than sleep a fixed amount: a fixed sleep either races the in-flight window or
        // burns most of the 250ms wait before probing. Bounded well inside that wait so a probe that
        // never finds the lock free fails loudly, rather than passing because timing drifted.
        IDistributedSynchronizationHandle? probe = null;
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(200);
        while (probe is null && DateTime.UtcNow < deadline)
        {
            probe = await locks.TryAcquireLockAsync(ReservationKeys.Lock(higherId), TimeSpan.Zero);
            if (probe is null)
                await Task.Delay(10);
        }

        // Free means the handler never touched the higher id before blocking on the held lower one —
        // proof it sorted ascending rather than taking the ids in the order it was asked for.
        Assert.NotNull(probe);
        await probe!.DisposeAsync();

        await Assert.ThrowsAsync<BookingsApplicationException>(() => send);

        var reservations = await Cache.GetByKeysAsync<ReserveTicketDto>(
            [ReservationKeys.Reservation(lowerId), ReservationKeys.Reservation(higherId)]);
        Assert.Empty(reservations);
    }

    [Fact]
    public async Task Releases_every_lock_it_took()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        var ids = tickets.Select(t => t.Id).ToArray();

        await Sender.Send(new ReserveTicketCommand("user-1", "evt-1", ids));

        var locks = Act.GetRequiredService<IDistributedLockProvider>();

        foreach (var id in ids)
        {
            // Acquirable immediately means the handler gave it back. A leaked lock strands the seat.
            await using var handle = await locks.TryAcquireLockAsync(
                ReservationKeys.Lock(id), TimeSpan.Zero);

            Assert.NotNull(handle);
        }
    }

    /// <summary>
    /// A lock taken before the unavailable one has to be released too. Leaving it behind strands a
    /// seat nobody is reserving.
    /// </summary>
    [Fact]
    public async Task Releases_the_locks_it_took_before_hitting_one_it_could_not_have()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        var ids = tickets.Select(t => t.Id).OrderBy(id => id).ToArray();
        var (first, second) = (ids[0], ids[1]);

        var locks = Act.GetRequiredService<IDistributedLockProvider>();
        await using var held = await locks.AcquireLockAsync(ReservationKeys.Lock(second));

        await Assert.ThrowsAsync<BookingsApplicationException>(() =>
            Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [first, second])));

        await using var handle = await locks.TryAcquireLockAsync(ReservationKeys.Lock(first), TimeSpan.Zero);
        Assert.NotNull(handle);
    }

    [Fact]
    public async Task Releases_its_locks_even_when_the_reservation_fails()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        var ids = tickets.Select(t => t.Id).ToArray();
        await Seed.ReservationAsync("other-user", "evt-1", ids[1]);

        await Assert.ThrowsAsync<BookingsApplicationException>(() =>
            Sender.Send(new ReserveTicketCommand("user-1", "evt-1", ids)));

        var locks = Act.GetRequiredService<IDistributedLockProvider>();
        foreach (var id in ids)
        {
            await using var handle = await locks.TryAcquireLockAsync(ReservationKeys.Lock(id), TimeSpan.Zero);
            Assert.NotNull(handle);
        }
    }

    // --- Rejected requests ---

    [Fact]
    public async Task Refuses_a_request_with_no_tickets()
    {
        await Assert.ThrowsAsync<BookingsDomainException>(() =>
            Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [])));
    }

    [Fact]
    public async Task Refuses_more_tickets_than_allowed()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2", "A3");

        await Assert.ThrowsAsync<BookingsDomainException>(() =>
            Sender.Send(new ReserveTicketCommand("user-1", "evt-1", tickets.Select(t => t.Id).ToArray())));
    }

    /// <summary>
    /// Redis locks are not reentrant, so the same ticket twice would have the request waiting on a
    /// lock it is already holding and then failing as though somebody else held it. The duplicate
    /// check runs before any lock is taken, so the ticket's lock must still be free afterwards.
    /// </summary>
    [Fact]
    public async Task Refuses_the_same_ticket_twice()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");

        await Assert.ThrowsAsync<BookingsDomainException>(() =>
            Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [tickets[0].Id, tickets[0].Id])));

        var locks = Act.GetRequiredService<IDistributedLockProvider>();
        await using var handle = await locks.TryAcquireLockAsync(ReservationKeys.Lock(tickets[0].Id), TimeSpan.Zero);
        Assert.NotNull(handle);
    }

    // --- The seat has to be real and for sale ---

    /// <summary>
    /// Reservation used to ask Redis whether the ticket was already reserved and nothing else, so a
    /// ticket id that had never existed reserved perfectly happily and failed at booking instead.
    /// </summary>
    [Fact]
    public async Task Refuses_a_ticket_that_does_not_exist()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [long.MaxValue])));

        Assert.Empty(await Cache.GetByKeysAsync<ReserveTicketDto>([ReservationKeys.Reservation(long.MaxValue)]));
    }

    [Fact]
    public async Task Refuses_when_only_some_of_the_tickets_exist()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [tickets[0].Id, long.MaxValue])));

        var held = await Cache.GetByKeysAsync<ReserveTicketDto>(
            [ReservationKeys.Reservation(tickets[0].Id), ReservationKeys.Reservation(long.MaxValue)]);
        Assert.Empty(held);
    }

    [Fact]
    public async Task Refuses_a_ticket_that_belongs_to_another_event()
    {
        var tickets = await Seed.TicketsAsync("evt-2", "B1");

        await Assert.ThrowsAsync<BookingsDomainException>(() =>
            Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [tickets[0].Id])));

        Assert.Empty(await Cache.GetByKeysAsync<ReserveTicketDto>([ReservationKeys.Reservation(tickets[0].Id)]));
    }

    /// <summary>
    /// Sold is sold, whether or not it has been paid for — paying does not change the ticket, it is
    /// already booked by then.
    /// </summary>
    [Fact]
    public async Task Refuses_a_ticket_that_is_already_sold()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        await Seed.BookingAsync("other-user", tickets[0].Id);

        await Assert.ThrowsAsync<BookingsApplicationException>(() =>
            Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [tickets[0].Id])));

        Assert.Empty(await Cache.GetByKeysAsync<ReserveTicketDto>([ReservationKeys.Reservation(tickets[0].Id)]));
    }

    [Fact]
    public async Task Refuses_a_ticket_the_event_has_cancelled()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        await CancelAsync(tickets[0].Id, eventVersion: 1);

        await Assert.ThrowsAsync<BookingsApplicationException>(() =>
            Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [tickets[0].Id])));

        Assert.Empty(await Cache.GetByKeysAsync<ReserveTicketDto>([ReservationKeys.Reservation(tickets[0].Id)]));
    }

    /// <summary>
    /// The other side of it: a seat released because its payment failed has to be holdable again, or a
    /// failed payment would take it out of circulation as surely as a successful one.
    /// </summary>
    [Fact]
    public async Task Allows_a_ticket_that_was_released_after_a_failed_payment()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1");
        await BookThenReleaseAsync(tickets[0].Id);

        await Sender.Send(new ReserveTicketCommand("user-1", "evt-1", [tickets[0].Id]));

        var held = await Cache.GetByKeysAsync<ReserveTicketDto>([ReservationKeys.Reservation(tickets[0].Id)]);
        Assert.Single(held);
    }

    [Fact]
    public async Task Reserves_nothing_when_one_of_the_tickets_is_unavailable()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        await Seed.BookingAsync("other-user", tickets[1].Id);

        await Assert.ThrowsAsync<BookingsApplicationException>(() =>
            Sender.Send(new ReserveTicketCommand("user-1", "evt-1", tickets.Select(t => t.Id).ToArray())));

        var held = await Cache.GetByKeysAsync<ReserveTicketDto>(
            tickets.Select(t => ReservationKeys.Reservation(t.Id)).ToArray());
        Assert.Empty(held);
    }

    [Fact]
    public async Task Releases_its_locks_when_a_ticket_is_unavailable()
    {
        var tickets = await Seed.TicketsAsync("evt-1", "A1", "A2");
        await Seed.BookingAsync("other-user", tickets[1].Id);

        await Assert.ThrowsAsync<BookingsApplicationException>(() =>
            Sender.Send(new ReserveTicketCommand("user-1", "evt-1", tickets.Select(t => t.Id).ToArray())));

        var locks = Act.GetRequiredService<IDistributedLockProvider>();
        foreach (var ticket in tickets)
        {
            await using var handle = await locks.TryAcquireLockAsync(ReservationKeys.Lock(ticket.Id), TimeSpan.Zero);
            Assert.NotNull(handle);
        }
    }

    // --- Arrangement that Seed does not cover: state reachable only by mutating the entity directly ---

    private async Task CancelAsync(long ticketId, long eventVersion)
    {
        await using var scope = Act.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BookingDomainContext>();
        var ticket = await context.Tickets.SingleAsync(t => t.Id == ticketId);
        ticket.Cancel(eventVersion);
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Saves between the two transitions so the row genuinely round-trips through Booked in Postgres —
    /// Book() immediately followed by Release() in one SaveChanges nets back to None and would leave
    /// the database never having held Booked at all, making this arrangement indistinguishable from a
    /// plain seeded ticket.
    /// </summary>
    private async Task BookThenReleaseAsync(long ticketId)
    {
        await using var scope = Act.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<BookingDomainContext>();
        var ticket = await context.Tickets.SingleAsync(t => t.Id == ticketId);

        ticket.Book();
        await context.SaveChangesAsync();

        ticket.Release();
        await context.SaveChangesAsync();
    }
}
