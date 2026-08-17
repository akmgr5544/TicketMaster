namespace Events.Application.Exceptions;

/// <summary>
/// The one application-layer exception: a use case cannot proceed even though the model itself is
/// intact. Public so the API can map it to a status code.
/// <para>
/// Distinct from <c>EventsDomainException</c>, which means an invariant of an entity was broken.
/// Nothing is wrong with the aggregate here — the request conflicts with the current state of the
/// world, so this maps to 409 Conflict.
/// </para>
/// </summary>
public class EventsApplicationException(string message) : Exception(message);
