namespace PaymentSystem.Query;

// No PspToken: it is the PSP's handle on the buyer's card and is never read back out.
public class PaymentMethodReadModel
{
    public Guid Id { get; init; }
    public string UserId { get; init; } = null!;
    public string PspName { get; init; } = null!;
    public bool IsDefault { get; init; }
}
