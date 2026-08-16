using Events.Domain.Entities;
using Events.Domain.Exceptions;
using Events.Domain.ValueObjects;

namespace EventsDomain;

public class VenueTests
{
    private static readonly GeoLocation Yerevan = new(40.1872, 44.5152);
    private static readonly GeoLocation Tbilisi = new(41.7151, 44.8271);

    private static Venue AVenue() => new("Karen Demirchyan Complex", "Tsitsernakaberd Hwy 1", Yerevan, ["A1", "A2"]);

    [Fact]
    public void Generates_its_own_id_on_creation()
    {
        var venue = AVenue();

        Assert.False(string.IsNullOrWhiteSpace(venue.Id));
    }

    [Fact]
    public void Gives_each_venue_a_distinct_id()
    {
        Assert.NotEqual(AVenue().Id, AVenue().Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_name(string name)
    {
        Assert.Throws<EventsDomainException>(() => new Venue(name, "an address", Yerevan, ["A1"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejects_a_blank_address(string address)
    {
        Assert.Throws<EventsDomainException>(() => new Venue("a name", address, Yerevan, ["A1"]));
    }

    [Fact]
    public void Rejects_a_venue_with_no_seats()
    {
        Assert.Throws<EventsDomainException>(() => new Venue("a name", "an address", Yerevan, []));
    }

    [Fact]
    public void Renames_the_venue()
    {
        var venue = AVenue();

        venue.Rename("Demirchyan Arena");

        Assert.Equal("Demirchyan Arena", venue.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuses_to_rename_to_a_blank_name(string name)
    {
        var venue = AVenue();

        Assert.Throws<EventsDomainException>(() => venue.Rename(name));
        Assert.Equal("Karen Demirchyan Complex", venue.Name);
    }

    [Fact]
    public void Relocates_the_venue()
    {
        var venue = AVenue();

        venue.Relocate(Tbilisi);

        Assert.Equal(Tbilisi, venue.Location);
    }

    [Fact]
    public void Does_not_share_seat_storage_with_the_caller()
    {
        var seats = new List<string> { "A1", "A2" };
        var venue = new Venue("a name", "an address", Yerevan, seats);

        seats.Add("A3");

        Assert.Equal(2, venue.Seats.Count);
    }
}
