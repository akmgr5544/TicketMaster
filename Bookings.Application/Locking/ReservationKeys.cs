namespace Bookings.Application.Locking;

/// <summary>
/// The Redis keys the reservation flow uses. Both the reserve and the booking handler read and write
/// these, so the format lives in one place.
/// </summary>
internal static class ReservationKeys
{
    /// <summary>
    /// Namespaced rather than the bare ticket id: an unqualified "12" collides with anything else
    /// sharing the Redis instance, and the reservation would be whatever wrote last.
    /// </summary>
    internal static string Reservation(long ticketId) => $"bookings:reservation:{ticketId}";

    /// <summary>
    /// One lock per ticket. The key has to name the contended resource — a single shared key puts
    /// every reservation in the service into one queue, however many different events they cover.
    /// </summary>
    internal static string Lock(long ticketId) => $"bookings:reserve:ticket:{ticketId}";
}
