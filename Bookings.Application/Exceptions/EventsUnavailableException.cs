namespace Bookings.Application.Exceptions;

public sealed class EventsUnavailableException(string message) : BookingsApplicationException(message);
