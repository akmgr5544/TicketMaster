using System.Collections.Immutable;
using Bookings.Domain.Abstractions;
using Bookings.Domain.Entities;

namespace Bookings.Domain.DomainEvents;

public record BookingCreatedDomainEvent(Ticket[] Tickets) : DomainEvent;