using Event = Events.Domain.Entities.Event;

namespace Events.Application.Commands;

// Who is asking, from the gateway's identity headers — never from the body. An admin may change any event, which is
// also what lets an event stored before organizers existed be changed at all.
public record Caller(Guid UserId, bool IsAdmin)
{
    public bool MayChange(Event @event) => IsAdmin || @event.IsOrganizedBy(UserId);
}
