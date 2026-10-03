namespace Events.Application.Exceptions;

/// <summary>
/// The caller may not change this. Not a 404: the catalogue is public, so hiding the event would hide nothing.
/// </summary>
public sealed class ForbiddenException(string entity, string id)
    : EventsApplicationException($"Only the organizer or an admin can change {entity} '{id}'");
