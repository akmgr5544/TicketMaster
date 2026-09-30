namespace PaymentSystem.Domain.Exceptions;

// An aggregate refused a change: a broken invariant or an illegal transition. Maps to 400.
public class PaymentDomainException(string message) : Exception(message);
