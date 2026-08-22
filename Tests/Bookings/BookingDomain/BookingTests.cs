using Bookings.Domain.DomainEvents;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;

namespace BookingDomain;

/// <summary>
/// These cover what <c>Booking.Create</c> is responsible for. The aggregate raises its own creation
/// event rather than having the handler assemble one afterwards, so that the event cannot be
/// forgotten by a new caller — and it carries ticket ids rather than <c>Ticket</c> instances, because
/// handing out another aggregate's entities is what let a handler mutate one behind its root's back.
/// </summary>
public class BookingTests
{
    private static readonly long[] TwoTickets = [7L, 9L];

    [Fact]
    public void Records_the_tickets_it_was_created_for()
    {
        var booking = Booking.Create("user-1", BookingStatus.Booked, TwoTickets);

        Assert.Equal(TwoTickets, booking.BookedTickets.Select(x => x.TicketId));
    }

    [Fact]
    public void Opens_its_history_with_the_status_it_was_created_in()
    {
        var booking = Booking.Create("user-1", BookingStatus.Booked, TwoTickets);

        var history = Assert.Single(booking.BookingHistories);
        Assert.Equal(BookingStatus.Booked, history.BookingStatus);
        Assert.Equal(2, history.TicketsCount);
    }

    [Fact]
    public void Raises_its_own_creation_event()
    {
        var booking = Booking.Create("user-1", BookingStatus.Booked, TwoTickets);

        var domainEvent = Assert.Single(booking.DomainEvents);
        Assert.IsType<BookingCreatedDomainEvent>(domainEvent);
    }

    /// <summary>
    /// Ids, not instances. A handler that receives <c>Ticket</c> objects can change them without
    /// going through the ticket's own root, which is exactly how the booked status used to be set and
    /// then lost.
    /// </summary>
    [Fact]
    public void Creation_event_carries_ticket_ids()
    {
        var booking = Booking.Create("user-1", BookingStatus.Booked, TwoTickets);

        var created = Assert.IsType<BookingCreatedDomainEvent>(Assert.Single(booking.DomainEvents));
        Assert.Equal(TwoTickets, created.TicketIds);
    }

    [Fact]
    public void Refuses_to_be_created_without_tickets()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Booking.Create("user-1", BookingStatus.Booked, []));
    }

    // --- Payment settled ---

    private static Booking ABooking() =>
        Booking.Create("user-1", BookingStatus.Booked, TwoTickets);

    [Fact]
    public void Becomes_paid_when_the_payment_succeeds()
    {
        var booking = ABooking();

        booking.MarkPaid();

        Assert.Equal(BookingStatus.Payed, booking.Status);
    }

    /// <summary>
    /// The history is what makes a booking's life legible after the fact, and until now nothing wrote
    /// to it after creation.
    /// </summary>
    [Fact]
    public void Records_the_payment_in_its_history()
    {
        var booking = ABooking();

        booking.MarkPaid();

        Assert.Equal([BookingStatus.Booked, BookingStatus.Payed],
            booking.BookingHistories.Select(x => x.BookingStatus));
    }

    /// <summary>
    /// Payment results are delivered at least once, so the same success may arrive twice. The second
    /// one must change nothing — including not adding a second history entry.
    /// </summary>
    [Fact]
    public void Paying_twice_is_the_same_as_paying_once()
    {
        var booking = ABooking();

        booking.MarkPaid();
        booking.MarkPaid();

        Assert.Equal(BookingStatus.Payed, booking.Status);
        Assert.Equal(2, booking.BookingHistories.Count);
    }

    [Fact]
    public void Is_cancelled_when_the_payment_fails()
    {
        var booking = ABooking();

        booking.Cancel();

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Equal([BookingStatus.Booked, BookingStatus.Cancelled],
            booking.BookingHistories.Select(x => x.BookingStatus));
    }

    /// <summary>
    /// Cancelling announces which seats to put back, by id — the tickets are their own aggregate and a
    /// handler reaches them through their own root rather than through this one.
    /// </summary>
    [Fact]
    public void Cancelling_announces_the_tickets_to_release()
    {
        var booking = ABooking();
        booking.ClearDomainEvents();

        booking.Cancel();

        var cancelled = Assert.IsType<BookingCancelledDomainEvent>(Assert.Single(booking.DomainEvents));
        Assert.Equal(TwoTickets, cancelled.TicketIds);
    }

    [Fact]
    public void Cancelling_twice_announces_the_release_once()
    {
        var booking = ABooking();
        booking.ClearDomainEvents();

        booking.Cancel();
        booking.Cancel();

        Assert.Single(booking.DomainEvents);
        Assert.Equal(2, booking.BookingHistories.Count);
    }

    /// <summary>
    /// The two payment outcomes race, and delivery is unordered. These two guards are what make the
    /// first outcome to land the one that sticks: a late failure cannot void a booking that has been
    /// paid for, and a late success cannot claim seats that have already gone back on sale.
    /// </summary>
    [Fact]
    public void A_paid_booking_is_not_cancelled_by_a_late_failure()
    {
        var booking = ABooking();
        booking.MarkPaid();

        Assert.Throws<InvalidOperationException>(() => booking.Cancel());
        Assert.Equal(BookingStatus.Payed, booking.Status);
    }

    [Fact]
    public void A_cancelled_booking_is_not_paid_by_a_late_success()
    {
        var booking = ABooking();
        booking.Cancel();

        Assert.Throws<InvalidOperationException>(() => booking.MarkPaid());
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }
}
