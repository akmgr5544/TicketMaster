using Events.Domain.Entities;
using Events.Domain.Exceptions;
using Events.Domain.ValueObjects;

namespace EventsDomain;

public class EventTests
{
    private static Venue AVenue() =>
        new("Karen Demirchyan Complex", "Tsitsernakaberd Hwy 1", new GeoLocation(40.1872, 44.5152), ["A1"]);

    private static Performer APerformer() => new("System of a Down", "Armenian-American rock band");

    private static DateTime FarEnoughOut => DateTime.UtcNow.AddDays(11);

    [Fact]
    public void Generates_its_own_id_on_creation()
    {
        var @event = new Event(FarEnoughOut, AVenue(), [APerformer()]);

        Assert.False(string.IsNullOrWhiteSpace(@event.Id));
    }

    [Fact]
    public void Rejects_a_start_date_inside_the_minimum_lead_time()
    {
        Assert.Throws<EventsDomainException>(() => new Event(DateTime.UtcNow.AddDays(9), AVenue(), [APerformer()]));
    }

    [Fact]
    public void Rejects_a_start_date_in_the_past()
    {
        Assert.Throws<EventsDomainException>(() => new Event(DateTime.UtcNow.AddDays(-1), AVenue(), [APerformer()]));
    }

    [Fact]
    public void Rejects_an_event_with_no_performers()
    {
        Assert.Throws<EventsDomainException>(() => new Event(FarEnoughOut, AVenue(), []));
    }

    [Fact]
    public void Embeds_the_venue_and_performers_it_was_created_with()
    {
        var venue = AVenue();
        var performer = APerformer();

        var @event = new Event(FarEnoughOut, venue, [performer]);

        Assert.Equal(venue.Id, @event.Venue.Id);
        Assert.Equal(performer.Id, Assert.Single(@event.Performers).Id);
    }

    [Fact]
    public void Reschedules_to_a_later_date()
    {
        var @event = new Event(FarEnoughOut, AVenue(), [APerformer()]);
        var newDate = DateTime.UtcNow.AddDays(30);

        @event.Reschedule(newDate);

        Assert.Equal(newDate, @event.StartDate);
    }

    [Fact]
    public void Refuses_to_reschedule_inside_the_minimum_lead_time()
    {
        var original = FarEnoughOut;
        var @event = new Event(original, AVenue(), [APerformer()]);

        Assert.Throws<EventsDomainException>(() => @event.Reschedule(DateTime.UtcNow.AddDays(9)));
        Assert.Equal(original, @event.StartDate);
    }

    [Fact]
    public void Does_not_share_performer_storage_with_the_caller()
    {
        var performers = new List<Performer> { APerformer() };
        var @event = new Event(FarEnoughOut, AVenue(), performers);

        performers.Add(APerformer());

        Assert.Single(@event.Performers);
    }
}
