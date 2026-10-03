using Bookings.Domain.Abstractions;
using Bookings.Domain.DomainEvents;
using MediatR;
using TicketMaster.Common.IntegrationEvents;

namespace Bookings.Application.DomainEventHandlers;

// Staged on the transaction that flagged the booking, so the request goes out only if the flag commits.
internal sealed class BookingRefundRequestedDomainEventHandler : INotificationHandler<BookingRefundRequestedDomainEvent>
{
    private readonly IIntegrationEventPublisher _integrationEvents;

    public BookingRefundRequestedDomainEventHandler(IIntegrationEventPublisher integrationEvents)
    {
        _integrationEvents = integrationEvents;
    }

    public Task Handle(BookingRefundRequestedDomainEvent notification, CancellationToken cancellationToken) =>
        _integrationEvents.PublishAsync(new RefundRequestedIntegrationEvent(notification.BookingId), cancellationToken);
}
