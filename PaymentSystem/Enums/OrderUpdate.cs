namespace PaymentSystem.Enums;

// What a provider's final answer about an order did to it.
public enum OrderUpdate
{
    Applied,
    // The order is already where the answer would take it: a redelivery.
    AlreadyApplied,
    // The order already settled the other way; a settled payment does not change its mind.
    Superseded,
    // The provider reports on an order this service has not started; asking again later can succeed.
    NotStarted
}
