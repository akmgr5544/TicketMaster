using Bookings.Application.Services.Interfaces;
using Bookings.Domain.DomainEvents;
using Bookings.Domain.Enums;
using MediatR;

namespace Bookings.Application.DomainEventHandlers;

public class BookingCreatedDomainEventHandler : INotificationHandler<BookingCreatedDomainEvent>
{
    public Task Handle(BookingCreatedDomainEvent notification, CancellationToken cancellationToken)
    {
        foreach (var ticket in notification.Tickets)
        {
            ticket.Status = TicketStatus.Booked;
        }
        return Task.CompletedTask;
    }
}