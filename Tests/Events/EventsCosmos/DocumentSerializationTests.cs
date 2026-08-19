using System.Text.Json;
using Events.Cosmos.Serialization;
using Events.Domain.Entities;
using Events.Domain.ValueObjects;

namespace EventsCosmos;

/// <summary>
/// These exercise the same <see cref="JsonSerializerOptions"/> the CosmosClient is configured with,
/// so they cover the real document shape without needing an emulator.
/// </summary>
public class DocumentSerializationTests
{
    private static readonly JsonSerializerOptions Options = CosmosJson.Options;

    private static Venue AVenue() =>
        new("Karen Demirchyan Complex", "Tsitsernakaberd Hwy 1", new GeoLocation(40.1872, 44.5152), ["A1", "A2"]);

    private static Performer APerformer() => new("System of a Down", "Armenian-American rock band");

    private static T RoundTrip<T>(T value) =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!;

    [Fact]
    public void Venue_survives_a_round_trip()
    {
        var venue = AVenue();

        var loaded = RoundTrip(venue);

        Assert.Equal(venue.Id, loaded.Id);
        Assert.Equal(venue.Name, loaded.Name);
        Assert.Equal(venue.Address, loaded.Address);
        Assert.Equal(venue.Location, loaded.Location);
        Assert.Equal(venue.Seats, loaded.Seats);
    }

    [Fact]
    public void Venue_is_written_with_the_lowercase_id_cosmos_requires()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(AVenue(), Options));

        Assert.True(document.RootElement.TryGetProperty("id", out _));
    }

    [Fact]
    public void Location_is_written_as_geojson_with_longitude_first()
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(AVenue(), Options));

        var location = document.RootElement.GetProperty("location");
        var coordinates = location.GetProperty("coordinates");

        Assert.Equal("Point", location.GetProperty("type").GetString());
        Assert.Equal(44.5152, coordinates[0].GetDouble());
        Assert.Equal(40.1872, coordinates[1].GetDouble());
    }

    [Fact]
    public void Performer_survives_a_round_trip()
    {
        var performer = APerformer();

        var loaded = RoundTrip(performer);

        Assert.Equal(performer.Id, loaded.Id);
        Assert.Equal(performer.Name, loaded.Name);
        Assert.Equal(performer.Description, loaded.Description);
    }

    [Fact]
    public void Event_survives_a_round_trip_with_its_embedded_snapshots()
    {
        var @event = new Event(DateTime.UtcNow.AddDays(11), AVenue(), [APerformer()]);

        var loaded = RoundTrip(@event);

        Assert.Equal(@event.Id, loaded.Id);
        Assert.Equal(@event.Venue.Id, loaded.Venue.Id);
        Assert.Equal(@event.Venue.Seats, loaded.Venue.Seats);
        Assert.Equal(@event.Performers.Single().Id, loaded.Performers.Single().Id);
    }

    /// <summary>
    /// Domain events are in-memory bookkeeping, not persisted state. They must not reach the
    /// document: they would bloat every write, cost RU for data nobody reads back, and re-publish
    /// nothing on load because there is no setter for them.
    /// </summary>
    [Fact]
    public void Event_does_not_write_its_domain_events_to_the_document()
    {
        var @event = new Event(DateTime.UtcNow.AddDays(11), AVenue(), [APerformer()]);

        var json = JsonSerializer.Serialize(@event, Options);

        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.TryGetProperty("domainEvents", out _), $"document was: {json}");
    }

    /// <summary>
    /// Stored as a name, not an ordinal. Reordering or inserting a value in the enum would silently
    /// reinterpret every document already written, and <c>c.status = "Cancelled"</c> is a query a
    /// human can read.
    /// </summary>
    [Fact]
    public void Event_status_is_written_as_a_name()
    {
        var @event = new Event(DateTime.UtcNow.AddDays(11), AVenue(), [APerformer()]);
        @event.Cancel();

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(@event, Options));

        Assert.Equal("Cancelled", document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void Event_round_trip_keeps_its_status_and_version()
    {
        var @event = new Event(DateTime.UtcNow.AddDays(11), AVenue(), [APerformer()]);
        @event.Reschedule(DateTime.UtcNow.AddDays(30));

        var loaded = RoundTrip(@event);

        Assert.Equal(@event.Version, loaded.Version);
        Assert.Equal(@event.Status, loaded.Status);
    }

    /// <summary>
    /// The regression guard for the rehydration trap: an event stored years ago must still load.
    /// If deserialization ever routes through the public constructor again, the minimum lead-time
    /// rule fires on read and every historical event becomes unreadable.
    /// </summary>
    [Fact]
    public void Event_that_has_already_happened_still_deserializes()
    {
        const string storedDocument = """
        {
          "id": "0192f3a1-0000-7000-8000-000000000001",
          "startDate": "2020-01-01T00:00:00Z",
          "venue": {
            "id": "0192f3a1-0000-7000-8000-000000000002",
            "name": "Karen Demirchyan Complex",
            "address": "Tsitsernakaberd Hwy 1",
            "location": { "type": "Point", "coordinates": [44.5152, 40.1872] },
            "seats": ["A1", "A2"]
          },
          "performers": [
            {
              "id": "0192f3a1-0000-7000-8000-000000000003",
              "name": "System of a Down",
              "description": "Armenian-American rock band"
            }
          ]
        }
        """;

        var loaded = JsonSerializer.Deserialize<Event>(storedDocument, Options)!;

        Assert.Equal(new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc), loaded.StartDate.ToUniversalTime());
        Assert.Equal("Karen Demirchyan Complex", loaded.Venue.Name);
        Assert.Equal(new GeoLocation(40.1872, 44.5152), loaded.Venue.Location);
        Assert.Single(loaded.Performers);
    }
}
