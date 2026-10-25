using PaymentSystem.Domain;
using PaymentSystem.Domain.Exceptions;

namespace PaymentDomain;

public sealed class PaymentMethodTests
{
    private static readonly Guid User = Guid.NewGuid();

    // D6
    [Fact]
    public void A_method_with_no_user_is_refused() =>
        Assert.Throws<PaymentDomainException>(() => PaymentMethod.Create(Guid.Empty, "Stripe", "pm_1"));

    [Theory]
    [InlineData("", "pm_1")]
    [InlineData(" ", "pm_1")]
    [InlineData("Stripe", "")]
    [InlineData("Stripe", "  ")]
    public void A_method_missing_its_provider_or_token_is_refused(string provider, string token) =>
        Assert.Throws<PaymentDomainException>(() => PaymentMethod.Create(User, provider, token));

    [Fact]
    public void A_new_method_carries_what_it_was_given_and_is_not_default()
    {
        var method = PaymentMethod.Create(User, "Stripe", "pm_1");

        Assert.NotEqual(Guid.Empty, method.Id);
        Assert.Equal(User, method.UserId);
        Assert.Equal("Stripe", method.PspName);
        Assert.Equal("pm_1", method.PspToken);
        Assert.False(method.IsDefault);
    }

    [Fact]
    public void A_method_marked_default_is_default_until_cleared()
    {
        var method = PaymentMethod.Create(User, "Stripe", "pm_1");

        method.MarkAsDefault();
        Assert.True(method.IsDefault);

        method.ClearDefault();
        Assert.False(method.IsDefault);
    }
}
