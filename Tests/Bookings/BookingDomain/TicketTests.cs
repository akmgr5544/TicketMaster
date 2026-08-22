using Bookings.Domain.Entities;
using Bookings.Domain.Enums;

namespace BookingDomain;

/// <summary>
/// These cover the staleness rule, which is what protects tickets from messages that arrive out of
/// order. Delivery is at-least-once and unordered, so a redelivered older change must not overwrite
/// a newer one.
/// </summary>
public class TicketTests
{
    private static Ticket ATicket(long eventVersion = 1) =>
        new("A1", "venue-1", "event-1", new DateTime(2030, 1, 1, 20, 0, 0, DateTimeKind.Utc), eventVersion);

    [Fact]
    public void Remembers_the_event_version_it_was_created_from()
    {
        var ticket = ATicket(eventVersion: 3);

        Assert.Equal(3, ticket.EventVersion);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(3, true)]
    [InlineData(2, true)]
    public void Treats_anything_not_newer_as_stale(long incoming, bool expected)
    {
        var ticket = ATicket(eventVersion: 3);

        Assert.Equal(expected, ticket.IsStale(incoming));
    }

    // --- Reschedule ---

    [Fact]
    public void Reschedules_to_a_newer_version()
    {
        var ticket = ATicket(eventVersion: 1);
        var newDate = new DateTime(2030, 6, 1, 20, 0, 0, DateTimeKind.Utc);

        ticket.Reschedule(newDate, eventVersion: 2);

        Assert.Equal(newDate, ticket.EventDate);
        Assert.Equal(2, ticket.EventVersion);
    }

    /// <summary>
    /// The guard lives on the entity rather than in the handler so that a consumer cannot forget it.
    /// </summary>
    [Fact]
    public void Ignores_a_reschedule_that_is_not_newer()
    {
        var ticket = ATicket(eventVersion: 5);
        var original = ticket.EventDate;

        ticket.Reschedule(new DateTime(2030, 6, 1, 20, 0, 0, DateTimeKind.Utc), eventVersion: 4);

        Assert.Equal(original, ticket.EventDate);
        Assert.Equal(5, ticket.EventVersion);
    }

    // --- Relocate ---

    [Fact]
    public void Relocates_to_a_newer_version()
    {
        var ticket = ATicket(eventVersion: 1);

        ticket.Relocate("venue-2", eventVersion: 2);

        Assert.Equal("venue-2", ticket.VenueId);
        Assert.Equal(2, ticket.EventVersion);
    }

    [Fact]
    public void Ignores_a_relocation_that_is_not_newer()
    {
        var ticket = ATicket(eventVersion: 5);

        ticket.Relocate("venue-2", eventVersion: 5);

        Assert.Equal("venue-1", ticket.VenueId);
    }

    // --- Cancel ---

    [Fact]
    public void Cancels_on_a_newer_version()
    {
        var ticket = ATicket(eventVersion: 1);

        ticket.Cancel(eventVersion: 2);

        Assert.Equal(TicketStatus.Cancelled, ticket.Status);
        Assert.Equal(2, ticket.EventVersion);
    }

    [Fact]
    public void Ignores_a_cancellation_that_is_not_newer()
    {
        var ticket = ATicket(eventVersion: 5);

        ticket.Cancel(eventVersion: 3);

        Assert.Equal(TicketStatus.None, ticket.Status);
    }

    /// <summary>
    /// Applying the same cancellation twice is at-least-once delivery doing its job, and must land in
    /// the same place as applying it once.
    /// </summary>
    [Fact]
    public void Stays_cancelled_when_the_same_cancellation_arrives_twice()
    {
        var ticket = ATicket(eventVersion: 1);

        ticket.Cancel(eventVersion: 2);
        ticket.Cancel(eventVersion: 2);

        Assert.Equal(TicketStatus.Cancelled, ticket.Status);
        Assert.Equal(2, ticket.EventVersion);
    }

    // --- Book ---

    /// <summary>
    /// Booking is what the reservation converts into, and it is the transition that used to be done
    /// with a public setter from another aggregate's event handler — which meant nothing persisted it.
    /// </summary>
    [Fact]
    public void Books_a_ticket_that_nobody_holds()
    {
        var ticket = ATicket();

        ticket.Book();

        Assert.Equal(TicketStatus.Booked, ticket.Status);
    }

    /// <summary>
    /// Booking is not a catalogue change, so it must not move the version — otherwise booking a ticket
    /// would make the next legitimate reschedule look stale and get discarded.
    /// </summary>
    [Fact]
    public void Booking_leaves_the_event_version_alone()
    {
        var ticket = ATicket(eventVersion: 4);

        ticket.Book();

        Assert.Equal(4, ticket.EventVersion);
    }

    [Fact]
    public void Refuses_to_book_a_cancelled_ticket()
    {
        var ticket = ATicket(eventVersion: 1);
        ticket.Cancel(eventVersion: 2);

        Assert.Throws<InvalidOperationException>(() => ticket.Book());
        Assert.Equal(TicketStatus.Cancelled, ticket.Status);
    }

    /// <summary>
    /// Two bookings for one seat is the failure the whole reservation dance exists to prevent, so the
    /// entity refuses it outright rather than letting the second one win silently.
    /// </summary>
    [Fact]
    public void Refuses_to_book_a_ticket_twice()
    {
        var ticket = ATicket();
        ticket.Book();

        Assert.Throws<InvalidOperationException>(() => ticket.Book());
    }

    // --- Release ---

    /// <summary>
    /// Payment failing has to put the seat back where it started, so somebody else can buy it. This is
    /// the only way out of <c>Booked</c> other than the event itself being called off.
    /// </summary>
    [Fact]
    public void Releases_a_booked_ticket_back_to_available()
    {
        var ticket = ATicket();
        ticket.Book();

        ticket.Release();

        Assert.Equal(TicketStatus.None, ticket.Status);
    }

    [Fact]
    public void Releasing_leaves_the_event_version_alone()
    {
        var ticket = ATicket(eventVersion: 4);
        ticket.Book();

        ticket.Release();

        Assert.Equal(4, ticket.EventVersion);
    }

    /// <summary>
    /// Payment results arrive at least once, so the same failure may be applied twice. The second
    /// application has to land in the same place as the first.
    /// </summary>
    [Fact]
    public void Releasing_an_available_ticket_changes_nothing()
    {
        var ticket = ATicket();

        ticket.Release();

        Assert.Equal(TicketStatus.None, ticket.Status);
    }

    /// <summary>
    /// A ticket cancelled because the event itself was called off must not come back to life just
    /// because a payment for it failed. The holder has already been told it is void, and the seat may
    /// no longer exist at all. Left alone rather than refused, so cancelling the booking still works.
    /// </summary>
    [Fact]
    public void Releasing_does_not_revive_a_cancelled_ticket()
    {
        var ticket = ATicket(eventVersion: 1);
        ticket.Cancel(eventVersion: 2);

        ticket.Release();

        Assert.Equal(TicketStatus.Cancelled, ticket.Status);
    }

    /// <summary>
    /// And a released seat can be booked again — otherwise a failed payment would take the seat out of
    /// circulation just as surely as a successful one.
    /// </summary>
    [Fact]
    public void A_released_ticket_can_be_booked_again()
    {
        var ticket = ATicket();
        ticket.Book();
        ticket.Release();

        ticket.Book();

        Assert.Equal(TicketStatus.Booked, ticket.Status);
    }

    // --- Availability ---

    /// <summary>
    /// Whether a seat can be held or sold at all. This is the question the reservation step used to
    /// skip entirely — it only asked Redis whether the ticket was already reserved, so a ticket that
    /// was sold, cancelled, or from another event reserved perfectly happily and failed later.
    /// </summary>
    private static readonly DateTime BeforeTheEvent = new(2029, 12, 31, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_untouched_ticket_is_available()
    {
        Assert.True(ATicket().IsAvailableFor("event-1", BeforeTheEvent));
    }

    [Fact]
    public void A_booked_ticket_is_not_available()
    {
        var ticket = ATicket();
        ticket.Book();

        Assert.False(ticket.IsAvailableFor("event-1", BeforeTheEvent));
    }

    /// <summary>
    /// Paying does not change the ticket — it is already booked — so a paid seat is unavailable for
    /// exactly the same reason a booked one is.
    /// </summary>
    [Fact]
    public void A_released_ticket_is_available_again()
    {
        var ticket = ATicket();
        ticket.Book();
        ticket.Release();

        Assert.True(ticket.IsAvailableFor("event-1", BeforeTheEvent));
    }

    [Fact]
    public void A_cancelled_ticket_is_not_available()
    {
        var ticket = ATicket(eventVersion: 1);
        ticket.Cancel(eventVersion: 2);

        Assert.False(ticket.IsAvailableFor("event-1", BeforeTheEvent));
    }

    /// <summary>
    /// A ticket belongs to one event. Asking for it under another event's id is a mismatched request,
    /// not an available seat.
    /// </summary>
    [Fact]
    public void A_ticket_is_not_available_for_a_different_event()
    {
        Assert.False(ATicket().IsAvailableFor("event-2", BeforeTheEvent));
    }

    /// <summary>
    /// Seats stay sellable for a while after the doors open, then stop. The window is
    /// <see cref="Ticket.SaleGracePeriod"/>; the ticket in these tests starts at 20:00 on 2030-01-01.
    /// </summary>
    [Theory]
    [InlineData(4, true)]
    [InlineData(6, false)]
    public void Availability_ends_a_while_after_the_event_starts(int hoursAfterStart, bool expected)
    {
        var ticket = ATicket();
        var asOf = new DateTime(2030, 1, 1, 20, 0, 0, DateTimeKind.Utc).AddHours(hoursAfterStart);

        Assert.Equal(expected, ticket.IsAvailableFor("event-1", asOf));
    }
}
