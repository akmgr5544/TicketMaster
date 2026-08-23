namespace Bookings.Domain.Exceptions;

/// <summary>
/// An entity refused a change, or a request was rejected before it reached the state of the world at
/// all: a broken invariant or a malformed request. Maps to 400.
/// <para>
/// Distinct from <c>BookingsApplicationException</c>, which means the request was well formed and the
/// model intact, but the world says no.
/// </para>
/// </summary>
public class BookingsDomainException(string message) : Exception(message);
