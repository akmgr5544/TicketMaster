using MediatR;

namespace Events.Application.Commands;

/// <summary>
/// One command per mutation rather than a single fat update, because each has different validation
/// and a different downstream consequence — a reschedule moves dates, a relocation changes which
/// seats exist at all. A combined command would have to infer which happened by diffing.
/// <para>
/// Every one carries the <see cref="Caller"/>, set by the controller; a command without one is refused, so a new
/// sender that forgets it fails closed.
/// </para>
/// </summary>
public record RescheduleEventCommand(string Id, DateTime StartDate, Caller? Caller = null) : IRequest;

public record RelocateEventCommand(string Id, string VenueId, Caller? Caller = null) : IRequest;

public record ChangeEventLineupCommand(string Id, List<string> PerformerIds, Caller? Caller = null) : IRequest;

/// <summary>
/// Replaces the whole pricing: the base price and every tier. A tier left out of the request is removed, so its
/// seats go back to the base price.
/// </summary>
public record RepriceEventCommand(string Id,
    decimal TicketPrice,
    string Currency,
    List<PriceTierRequest>? PriceTiers = null,
    Caller? Caller = null) : IRequest;

/// <summary>
/// Calls the event off without removing it. Idempotent — cancelling twice succeeds and announces
/// nothing the second time.
/// </summary>
public record CancelEventCommand(string Id, Caller? Caller = null) : IRequest;
