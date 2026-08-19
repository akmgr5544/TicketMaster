namespace Events.Domain.Enums;

public enum EventStatus
{
    Scheduled,

    /// <summary>
    /// Called off. The document survives so the history stays readable, and downstream tickets are
    /// cancelled rather than deleted.
    /// </summary>
    Cancelled
}
