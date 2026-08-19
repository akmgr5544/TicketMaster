namespace Events.Domain.Abstractions;

/// <summary>
/// Something that happened to an aggregate, recorded inside this service.
/// <para>
/// Deliberately a bare marker with no base type: Bookings' equivalent implements MediatR's
/// <c>INotification</c>, and copying that here would put a package reference on Events.Domain,
/// which has none by design. A domain event is translated into an integration event in
/// Events.Application before it ever reaches the broker — it is never published directly.
/// </para>
/// </summary>
public interface IDomainEvent;
