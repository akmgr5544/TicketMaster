namespace Events.Domain.Repositories;

/// <summary>
/// One page of results plus an opaque cursor for the next one, or null when the results are
/// exhausted. Cursor paging rather than skip/take because skipping is charged for what it skips.
/// </summary>
public sealed record Page<T>(IReadOnlyList<T> Items, string? ContinuationToken);
