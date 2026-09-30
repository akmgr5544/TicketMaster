using PaymentSystem.Domain.Abstractions;

namespace PaymentSystem.Domain.Events;

public sealed record PaymentOrderSucceededDomainEvent(Guid PaymentOrderId, Guid CheckoutId) : DomainEvent;
