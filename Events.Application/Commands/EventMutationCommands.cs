using MediatR;

namespace Events.Application.Commands;

/// <summary>
/// One command per mutation rather than a single fat update, because each has different validation
/// and a different downstream consequence — a reschedule moves dates, a relocation changes which
/// seats exist at all. A combined command would have to infer which happened by diffing.
/// </summary>
public record RescheduleEventCommand(string Id, DateTime StartDate) : IRequest;

public record RelocateEventCommand(string Id, string VenueId) : IRequest;

public record ChangeEventLineupCommand(string Id, List<string> PerformerIds) : IRequest;

/// <summary>
/// Calls the event off without removing it. Idempotent — cancelling twice succeeds and announces
/// nothing the second time.
/// </summary>
public record CancelEventCommand(string Id) : IRequest;
