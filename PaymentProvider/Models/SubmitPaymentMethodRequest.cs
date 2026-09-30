namespace PaymentProvider.Models;

// PaymentMethod is the single-use token the client's payment form produced (a Braintree nonce).
public sealed record SubmitPaymentMethodRequest(Guid PaymentOrderId, decimal Amount, string Currency, string PaymentMethod);
