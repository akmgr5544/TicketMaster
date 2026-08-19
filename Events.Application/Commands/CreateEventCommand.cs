using MediatR;

namespace Events.Application.Commands;

/// <summary>Returns the id of the created event so the caller can address it.</summary>
public record CreateEventCommand(DateTime StartDate,
    string Venue,
    List<string> Performers) : IRequest<string>;