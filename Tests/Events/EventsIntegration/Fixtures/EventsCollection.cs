namespace EventsIntegration.Fixtures;

// One collection for the whole project. xUnit parallelises across collections, and a single shared
// Cosmos database cannot survive that — the same reason BookingIntegration is one collection.
[CollectionDefinition(Name)]
public sealed class EventsCollection : ICollectionFixture<EventsFixture>
{
    public const string Name = "Events integration";
}
