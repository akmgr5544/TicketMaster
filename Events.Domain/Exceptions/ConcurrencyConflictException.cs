namespace Events.Domain.Exceptions;

public sealed class ConcurrencyConflictException(string entity, string id, Exception? innerException = null)
    : Exception($"{entity} with id '{id}' was modified by another request", innerException);
