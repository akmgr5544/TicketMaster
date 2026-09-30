namespace PaymentIntegration.Fixtures;

// One collection for the whole project. xUnit parallelises across collections, and a single shared
// database cannot survive that.
[CollectionDefinition(Name)]
public sealed class PaymentsCollection : ICollectionFixture<PaymentsFixture>
{
    public const string Name = "Payments integration";
}
