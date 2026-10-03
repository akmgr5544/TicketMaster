using MediatR;

namespace Events.Application.Commands;

/// <summary>
/// Returns the id of the created event so the caller can address it. <see cref="OrganizerId"/> is the
/// caller's own id, set by the controller from the gateway's identity header — never from the body.
/// </summary>
public record CreateEventCommand(DateTime StartDate,
    string Venue,
    List<string> Performers,
    decimal TicketPrice,
    string Currency,
    Guid OrganizerId = default) : IRequest<string>;
