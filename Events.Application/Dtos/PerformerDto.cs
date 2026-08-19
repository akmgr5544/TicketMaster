using Events.Domain.Entities;

namespace Events.Application.Dtos;

public record PerformerDto(string Id, string Name, string Description)
{
    public static PerformerDto From(Performer performer) => new(performer.Id,
        performer.Name,
        performer.Description);
}
