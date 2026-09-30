using PaymentSystem.Domain.Abstractions;

namespace PaymentSystem.Domain.Events;

public sealed record PaymentOrderFailedDomainEvent(Guid PaymentOrderId, Guid CheckoutId) : DomainEvent;
