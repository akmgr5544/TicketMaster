namespace Events.Application.Dtos;

/// <summary>
/// Send <see cref="ContinuationToken"/> back on the next request to continue where this page
/// stopped. A null token means there is nothing more to read.
/// </summary>
public record PagedResult<T>(IReadOnlyList<T> Items, string? ContinuationToken);
