namespace BookingIntegration.Fixtures;

// One collection for the whole project. xUnit parallelises across collections, and a single shared
// database cannot survive that.
[CollectionDefinition(Name)]
public sealed class BookingsCollection : ICollectionFixture<BookingsFixture>
{
    public const string Name = "Bookings integration";
}
