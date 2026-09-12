namespace BookingIntegration.Fixtures;

/// <summary>
/// Fixed caller ids shared across the suite. Bookings stores the user id as a <see cref="Guid"/>, so
/// tests need real Guids rather than the "user-1"/"user-2" strings they used before. Fixed values keep
/// failures reproducible; <c>Owner</c> and <c>Stranger</c> are distinct so authorization scoping tests
/// can tell one caller's bookings from another's.
/// </summary>
public static class TestUsers
{
    public static readonly Guid Owner = new("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Stranger = new("22222222-2222-2222-2222-222222222222");
}
