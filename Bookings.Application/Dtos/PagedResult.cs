namespace Bookings.Application.Dtos;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total)
{
    public bool HasMore => (long)Page * PageSize < Total;
}
