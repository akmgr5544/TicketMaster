using Events.Domain.Exceptions;
using Events.Domain.ValueObjects;

namespace EventsDomain;

public class GeoLocationTests
{
    [Fact]
    public void Exposes_the_coordinates_it_was_created_with()
    {
        var location = new GeoLocation(40.1872, 44.5152);

        Assert.Equal(40.1872, location.Latitude);
        Assert.Equal(44.5152, location.Longitude);
    }

    [Theory]
    [InlineData(90.1)]
    [InlineData(-90.1)]
    [InlineData(1000)]
    public void Rejects_latitude_outside_plus_or_minus_90(double latitude)
    {
        Assert.Throws<EventsDomainException>(() => new GeoLocation(latitude, 0));
    }

    [Theory]
    [InlineData(180.1)]
    [InlineData(-180.1)]
    [InlineData(1000)]
    public void Rejects_longitude_outside_plus_or_minus_180(double longitude)
    {
        Assert.Throws<EventsDomainException>(() => new GeoLocation(0, longitude));
    }

    [Theory]
    [InlineData(90, 180)]
    [InlineData(-90, -180)]
    public void Accepts_the_boundary_coordinates(double latitude, double longitude)
    {
        var location = new GeoLocation(latitude, longitude);

        Assert.Equal(latitude, location.Latitude);
        Assert.Equal(longitude, location.Longitude);
    }

    [Theory]
    [InlineData(double.NaN, 0)]
    [InlineData(0, double.NaN)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(0, double.NegativeInfinity)]
    public void Rejects_coordinates_that_are_not_finite(double latitude, double longitude)
    {
        Assert.Throws<EventsDomainException>(() => new GeoLocation(latitude, longitude));
    }
}
