using Events.Domain.DomainEvents;
using Events.Domain.Entities;
using Events.Domain.Exceptions;
using Events.Domain.ValueObjects;

namespace EventsDomain;

public class PriceTierTests
{
    private static readonly TicketPrice Base = new(50m, "USD");
    private static readonly Guid Organizer = Guid.CreateVersion7();

    private static Venue AVenue(params string[] seats) =>
        new("Karen Demirchyan Complex", "Tsitsernakaberd Hwy 1", new GeoLocation(40.1872, 44.5152),
            seats.Length == 0 ? ["A1", "A2", "B1", "B2"] : seats);

    private static Performer APerformer() => new("System of a Down", "Armenian-American rock band");

    private static Event AnEvent(params PriceTier[] tiers) =>
        new(DateTime.UtcNow.AddDays(11), AVenue(), [APerformer()], Base, Organizer, tiers);

    // --- The tier ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_tier_needs_a_name(string name) =>
        Assert.Throws<EventsDomainException>(() => new PriceTier(name, 10m, ["A1"]));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_tier_needs_a_positive_price(decimal amount) =>
        Assert.Throws<EventsDomainException>(() => new PriceTier("VIP", amount, ["A1"]));

    [Fact]
    public void A_tier_needs_at_least_one_seat() =>
        Assert.Throws<EventsDomainException>(() => new PriceTier("VIP", 10m, []));

    // --- Tiers on an event ---

    [Fact]
    public void An_event_keeps_the_tiers_it_was_created_with_and_announces_them()
    {
        var @event = AnEvent(new PriceTier("VIP", 120m, ["A1", "A2"]));

        var tier = Assert.Single(@event.PriceTiers);
        Assert.Equal("VIP", tier.Name);
        Assert.Equal(["A1", "A2"], tier.Seats);
        Assert.Single(Assert.Single(@event.DomainEvents.OfType<EventCreatedDomainEvent>()).PriceTiers);
    }

    [Fact]
    public void A_tier_cannot_price_a_seat_the_venue_does_not_have() =>
        Assert.Throws<EventsDomainException>(() => AnEvent(new PriceTier("VIP", 120m, ["Z9"])));

    // A seat has one price.
    [Fact]
    public void A_seat_cannot_sit_in_two_tiers() =>
        Assert.Throws<EventsDomainException>(() =>
            AnEvent(new PriceTier("VIP", 120m, ["A1"]), new PriceTier("Front", 90m, ["A1"])));

    [Fact]
    public void Two_tiers_cannot_share_a_name() =>
        Assert.Throws<EventsDomainException>(() =>
            AnEvent(new PriceTier("VIP", 120m, ["A1"]), new PriceTier("vip", 90m, ["A2"])));

    // --- Repricing ---

    [Fact]
    public void Repricing_replaces_the_whole_pricing_and_announces_it_with_a_new_version()
    {
        var @event = AnEvent(new PriceTier("VIP", 120m, ["A1"]));
        @event.ClearDomainEvents();

        @event.Reprice(new TicketPrice(60m, "USD"), [new PriceTier("Front", 80m, ["B1", "B2"])]);

        Assert.Equal(new TicketPrice(60m, "USD"), @event.TicketPrice);
        Assert.Equal("Front", Assert.Single(@event.PriceTiers).Name);
        var repriced = Assert.Single(@event.DomainEvents.OfType<EventRepricedDomainEvent>());
        Assert.Equal(2, repriced.Version);
        Assert.Equal(Organizer, repriced.OrganizerId);
        Assert.Equal(60m, repriced.TicketPrice.Amount);
    }

    [Fact]
    public void A_refused_repricing_leaves_the_pricing_and_version_as_they_were()
    {
        var @event = AnEvent(new PriceTier("VIP", 120m, ["A1"]));
        @event.ClearDomainEvents();

        Assert.Throws<EventsDomainException>(() =>
            @event.Reprice(new TicketPrice(60m, "USD"), [new PriceTier("Ghost", 80m, ["Z9"])]));

        Assert.Equal(Base, @event.TicketPrice);
        Assert.Equal("VIP", Assert.Single(@event.PriceTiers).Name);
        Assert.Equal(1, @event.Version);
        Assert.Empty(@event.DomainEvents);
    }

    [Fact]
    public void A_cancelled_event_cannot_be_repriced()
    {
        var @event = AnEvent();
        @event.Cancel();

        Assert.Throws<EventsDomainException>(() => @event.Reprice(new TicketPrice(60m, "USD"), []));
    }

    // --- Relocation ---

    // The new venue decides which seats exist; a tier keeps the ones still there and goes once it has none.
    [Fact]
    public void Relocating_narrows_each_tier_to_the_new_venues_seats()
    {
        var @event = AnEvent(new PriceTier("VIP", 120m, ["A1", "A2"]), new PriceTier("Back", 30m, ["B1"]));

        @event.Relocate(AVenue("A1", "C1"));

        var tier = Assert.Single(@event.PriceTiers);
        Assert.Equal("VIP", tier.Name);
        Assert.Equal(["A1"], tier.Seats);
        Assert.Single(Assert.Single(@event.DomainEvents.OfType<EventRelocatedDomainEvent>()).PriceTiers);
    }
}
