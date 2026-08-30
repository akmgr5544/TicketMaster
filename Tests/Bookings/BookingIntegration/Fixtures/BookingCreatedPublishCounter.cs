using Bookings.Domain.DomainEvents;
using MediatR;

namespace BookingIntegration.Fixtures;

/// <summary>
/// A second, counting handler for <see cref="BookingCreatedDomainEvent"/>, registered by
/// <see cref="BookingsFixture"/> alongside the real <c>BookingCreatedDomainEventHandler</c> — MediatR
/// invokes every registered handler for a notification, so this runs without altering the real one.
/// <para>
/// Exists so a mechanics test can observe how many times the interceptor actually published, without
/// a hand-built <c>IPublisher</c>, which would also mean a hand-built — and therefore off-transaction
/// — <c>BookingDomainContext</c>. Registered scoped, so each test's <c>Act</c> scope gets its own
/// counter and nothing leaks between tests.
/// </para>
/// </summary>
public sealed class BookingCreatedPublishCounter : INotificationHandler<BookingCreatedDomainEvent>
{
    public int Count { get; private set; }

    public Task Handle(BookingCreatedDomainEvent notification, CancellationToken cancellationToken)
    {
        Count++;
        return Task.CompletedTask;
    }
}
