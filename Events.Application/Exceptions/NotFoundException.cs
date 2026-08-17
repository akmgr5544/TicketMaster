namespace Events.Application.Exceptions;

/// <summary>
/// Something was asked for by id and is not there. Deliberately not <c>KeyNotFoundException</c>:
/// that is what a <c>Dictionary</c> indexer throws, so mapping it to 404 would turn any stray
/// lookup bug in our own code into a "not found" for the caller instead of a visible failure.
/// </summary>
public sealed class NotFoundException(string entity, string id)
    : EventsApplicationException($"{entity} with id '{id}' was not found");
