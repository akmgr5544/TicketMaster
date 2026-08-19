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
}
