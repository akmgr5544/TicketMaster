using Events.Domain.Exceptions;

namespace Events.Domain.ValueObjects;

/// <summary>
/// A point on the earth's surface. Latitude and longitude are stored in the order a human reads
/// them; GeoJSON's reversed [longitude, latitude] order is a persistence concern and is handled at
/// the serialization boundary, not here.
/// </summary>
public readonly record struct GeoLocation
{
    public GeoLocation(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || latitude is < -90 or > 90)
            throw new EventsDomainException($"Latitude must be a finite value between -90 and 90, but was {latitude}");

        if (!double.IsFinite(longitude) || longitude is < -180 or > 180)
            throw new EventsDomainException($"Longitude must be a finite value between -180 and 180, but was {longitude}");

        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }
    public double Longitude { get; }
}
