namespace Events.Domain.Exceptions;

public class EventsDomainException(string message) : Exception(message);