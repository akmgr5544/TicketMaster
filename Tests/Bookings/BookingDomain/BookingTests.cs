using Bookings.Domain.DomainEvents;
using Bookings.Domain.Entities;
using Bookings.Domain.Enums;
using Bookings.Domain.Exceptions;

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
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public void Records_the_tickets_it_was_created_for()
    {
        var booking = Booking.Create(User, BookingStatus.Booked, TwoTickets);

        Assert.Equal(TwoTickets, booking.BookedTickets.Select(x => x.TicketId));
    }

    [Fact]
    public void Opens_its_history_with_the_status_it_was_created_in()
    {
        var booking = Booking.Create(User, BookingStatus.Booked, TwoTickets);

        var history = Assert.Single(booking.BookingHistories);
        Assert.Equal(BookingStatus.Booked, history.BookingStatus);
        Assert.Equal(2, history.TicketsCount);
    }

    [Fact]
    public void Raises_its_own_creation_event()
    {
        var booking = Booking.Create(User, BookingStatus.Booked, TwoTickets);

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
        var booking = Booking.Create(User, BookingStatus.Booked, TwoTickets);

        var created = Assert.IsType<BookingCreatedDomainEvent>(Assert.Single(booking.DomainEvents));
        Assert.Equal(TwoTickets, created.TicketIds);
    }

    [Fact]
    public void Refuses_to_be_created_without_tickets()
    {
        Assert.Throws<BookingsDomainException>(() =>
            Booking.Create(User, BookingStatus.Booked, []));
    }

    // --- Payment settled ---

    private static Booking ABooking() =>
        Booking.Create(User, BookingStatus.Booked, TwoTickets);

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

        Assert.Throws<BookingsDomainException>(() => booking.Cancel());
        Assert.Equal(BookingStatus.Payed, booking.Status);
    }

    [Fact]
    public void A_cancelled_booking_is_not_paid_by_a_late_success()
    {
        var booking = ABooking();
        booking.Cancel();

        Assert.Throws<BookingsDomainException>(() => booking.MarkPaid());
        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }

    // --- A relocation cancels a seat this booking was holding ---

    [Fact]
    public void An_unpaid_booking_is_cancelled_when_a_booked_seat_is_lost()
    {
        var booking = ABooking();

        booking.OnBookedSeatCancelled();

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
    }

    [Fact]
    public void A_paid_booking_is_flagged_for_refund_when_a_booked_seat_is_lost()
    {
        var booking = ABooking();
        booking.MarkPaid();

        booking.OnBookedSeatCancelled();

        Assert.Equal(BookingStatus.RefundPending, booking.Status);
        Assert.Equal(BookingStatus.RefundPending, booking.BookingHistories[^1].BookingStatus);
    }

    [Fact]
    public void A_further_lost_seat_on_an_already_resolved_booking_changes_nothing()
    {
        var booking = ABooking();
        booking.MarkPaid();
        booking.OnBookedSeatCancelled();
        var historyCount = booking.BookingHistories.Count;

        booking.OnBookedSeatCancelled();

        Assert.Equal(BookingStatus.RefundPending, booking.Status);
        Assert.Equal(historyCount, booking.BookingHistories.Count);
    }

    // --- Refunds ---

    // Asked once: a second lost seat leaves the booking where it is, so it does not ask the payment service again.
    [Fact]
    public void Flagging_a_paid_booking_for_refund_asks_for_the_refund_once()
    {
        var booking = APaidBooking();

        booking.OnBookedSeatCancelled();
        booking.OnBookedSeatCancelled();

        Assert.Single(booking.DomainEvents.OfType<BookingRefundRequestedDomainEvent>());
    }

    [Fact]
    public void A_refund_completes_a_booking_waiting_for_one_and_releases_its_seats()
    {
        var booking = APaidBooking();
        booking.OnBookedSeatCancelled();
        booking.ClearDomainEvents();

        booking.MarkRefunded();

        Assert.Equal(BookingStatus.Refunded, booking.Status);
        Assert.Equal(BookingStatus.Refunded, booking.BookingHistories[^1].BookingStatus);
        var refunded = Assert.Single(booking.DomainEvents.OfType<BookingRefundedDomainEvent>());
        Assert.Equal(TwoTickets, refunded.TicketIds);
    }

    [Fact]
    public void A_refund_arriving_twice_releases_the_seats_once()
    {
        var booking = APaidBooking();
        booking.OnBookedSeatCancelled();
        booking.MarkRefunded();
        booking.ClearDomainEvents();
        var historyCount = booking.BookingHistories.Count;

        booking.MarkRefunded();

        Assert.Empty(booking.DomainEvents);
        Assert.Equal(historyCount, booking.BookingHistories.Count);
    }

    // Cancelled before its payment's success was heard: the seats went back then, only the money had to follow.
    [Fact]
    public void A_refund_of_a_cancelled_booking_changes_nothing()
    {
        var booking = ABooking();
        booking.Cancel();
        booking.ClearDomainEvents();

        booking.MarkRefunded();

        Assert.Equal(BookingStatus.Cancelled, booking.Status);
        Assert.Empty(booking.DomainEvents);
    }

    [Fact]
    public void A_booking_not_waiting_for_a_refund_refuses_one()
    {
        Assert.Throws<BookingsDomainException>(() => ABooking().MarkRefunded());
        Assert.Throws<BookingsDomainException>(() => APaidBooking().MarkRefunded());
    }

    // --- A customer cancelling seats of a paid booking ---

    // The seats stay held until the money is back, so a refund that fails has not already sold them to somebody else.
    [Fact]
    public void Cancelling_some_seats_of_a_paid_booking_asks_for_their_refund_and_keeps_the_booking_paid()
    {
        var booking = APaidBooking();

        var refundId = booking.RequestRefund([7L], 30m, "USD");

        Assert.Equal(BookingStatus.Payed, booking.Status);
        var requested = Assert.Single(booking.DomainEvents.OfType<BookingRefundRequestedDomainEvent>());
        Assert.Equal((refundId, (decimal?)30m, "USD"), (requested.RefundId, requested.Amount, requested.Currency));
        var refund = Assert.Single(booking.Refunds);
        Assert.Equal((refundId, BookingRefundStatus.Pending), (refund.Id, refund.Status));
        Assert.Equal([7L], booking.TicketsCoveredBy(refundId));
        Assert.DoesNotContain(booking.DomainEvents, e => e is BookingRefundedDomainEvent);
    }

    [Fact]
    public void Only_seats_the_booking_holds_and_no_refund_covers_can_be_refunded()
    {
        var booking = APaidBooking();
        booking.RequestRefund([7L], 30m, "USD");

        Assert.Throws<BookingsDomainException>(() => booking.RequestRefund([7L], 30m, "USD"));
        Assert.Throws<BookingsDomainException>(() => booking.RequestRefund([42L], 30m, "USD"));
        Assert.Throws<BookingsDomainException>(() => booking.RequestRefund([9L, 9L], 30m, "USD"));
        Assert.Throws<BookingsDomainException>(() => booking.RequestRefund([], 30m, "USD"));
        Assert.Single(booking.Refunds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_refund_must_give_something_back(decimal amount)
    {
        Assert.Throws<BookingsDomainException>(() => APaidBooking().RequestRefund([7L], amount, "USD"));
    }

    // An unpaid booking took no money; it is cancelled whole instead.
    [Fact]
    public void Only_a_paid_booking_is_refunded_seat_by_seat()
    {
        Assert.Throws<BookingsDomainException>(() => ABooking().RequestRefund([7L], 30m, "USD"));
    }

    [Fact]
    public void A_refund_landing_releases_its_own_seats_and_leaves_the_rest_paid()
    {
        var booking = APaidBooking();
        var refundId = booking.RequestRefund([7L], 30m, "USD");
        booking.ClearDomainEvents();

        booking.MarkRefunded(refundId);

        Assert.Equal(BookingStatus.Payed, booking.Status);
        Assert.Equal(BookingRefundStatus.Completed, Assert.Single(booking.Refunds).Status);
        Assert.Equal([7L], Assert.Single(booking.DomainEvents.OfType<BookingRefundedDomainEvent>()).TicketIds);
    }

    [Fact]
    public void A_booking_whose_every_seat_is_refunded_is_refunded()
    {
        var booking = APaidBooking();
        var first = booking.RequestRefund([7L], 30m, "USD");
        var second = booking.RequestRefund([9L], 30m, "USD");

        booking.MarkRefunded(first);
        booking.MarkRefunded(second);

        Assert.Equal(BookingStatus.Refunded, booking.Status);
        Assert.Equal(BookingStatus.Refunded, booking.BookingHistories[^1].BookingStatus);
    }

    [Fact]
    public void A_refund_landing_twice_releases_its_seats_once()
    {
        var booking = APaidBooking();
        var refundId = booking.RequestRefund([7L], 30m, "USD");
        booking.MarkRefunded(refundId);
        booking.ClearDomainEvents();

        booking.MarkRefunded(refundId);

        Assert.Empty(booking.DomainEvents);
    }

    [Fact]
    public void A_refund_the_booking_never_asked_for_is_refused()
    {
        Assert.Throws<BookingsDomainException>(() => APaidBooking().MarkRefunded(Guid.NewGuid()));
    }

    // --- A relocation voiding a booking that already has refunds ---

    [Fact]
    public void A_relocation_asks_for_whatever_is_left_and_names_the_refund()
    {
        var booking = APaidBooking();
        booking.RequestRefund([7L], 30m, "USD");
        booking.ClearDomainEvents();

        booking.OnBookedSeatCancelled();

        var requested = Assert.Single(booking.DomainEvents.OfType<BookingRefundRequestedDomainEvent>());
        Assert.Null(requested.Amount);
        Assert.Equal([9L], booking.TicketsCoveredBy(requested.RefundId));
    }

    // The whole-booking refund gives back everything still paid, the pending part's money included, so it settles
    // that part too: its seats are released and the booking is done.
    [Fact]
    public void The_whole_booking_refund_landing_settles_every_refund_still_pending()
    {
        var booking = APaidBooking();
        var part = booking.RequestRefund([7L], 30m, "USD");
        booking.OnBookedSeatCancelled();
        var whole = booking.DomainEvents.OfType<BookingRefundRequestedDomainEvent>().Last().RefundId;
        booking.ClearDomainEvents();

        booking.MarkRefunded(whole);

        Assert.Equal(BookingStatus.Refunded, booking.Status);
        Assert.All(booking.Refunds, refund => Assert.Equal(BookingRefundStatus.Completed, refund.Status));
        Assert.Equal(TwoTickets, Assert.Single(booking.DomainEvents.OfType<BookingRefundedDomainEvent>()).TicketIds.Order());
        booking.ClearDomainEvents();

        booking.MarkRefunded(part);

        Assert.Empty(booking.DomainEvents);
    }

    // A seat released by an earlier refund may since have been sold to somebody else; releasing it again would
    // take it from them.
    [Fact]
    public void The_whole_booking_refund_does_not_release_seats_an_earlier_refund_gave_back()
    {
        var booking = APaidBooking();
        booking.MarkRefunded(booking.RequestRefund([7L], 30m, "USD"));
        booking.OnBookedSeatCancelled();
        var whole = booking.DomainEvents.OfType<BookingRefundRequestedDomainEvent>().Last().RefundId;
        booking.ClearDomainEvents();

        booking.MarkRefunded(whole);

        Assert.Equal([9L], Assert.Single(booking.DomainEvents.OfType<BookingRefundedDomainEvent>()).TicketIds);
        Assert.Equal(BookingStatus.Refunded, booking.Status);
    }

    private static Booking APaidBooking()
    {
        var booking = ABooking();
        booking.MarkPaid();
        return booking;
    }
}
