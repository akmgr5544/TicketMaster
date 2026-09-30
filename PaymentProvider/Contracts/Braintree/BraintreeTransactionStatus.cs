namespace PaymentProvider.Contracts.Braintree;

internal enum BraintreeTransactionStatus
{
    Authorizing,
    Authorized,
    SubmittedForSettlement,
    Settling,
    SettlementPending,
    SettlementConfirmed,
    Settled,
    SettlementDeclined,
    ProcessorDeclined,
    GatewayRejected,
    Failed,
    Voided,
    AuthorizationExpired,
    Unknown,
}
