namespace PaymentSystem.Query;

public class PaymentMethodReadModel
{
    public Guid Id { get; init; }
    public Guid UserId { get; init; }
    public string PspName { get; init; } = null!;
    // The PSP's handle on the buyer's card, read only to charge it; never returned by an endpoint.
    public string PspToken { get; init; } = null!;
    public bool IsDefault { get; init; }
}
