using PaymentSystem.Domain;
using PaymentSystem.Domain.Exceptions;

namespace PaymentDomain;

public sealed class PaymentMethodTests
{
    [Theory]
    [InlineData("", "Stripe", "pm_1")]
    [InlineData("  ", "Stripe", "pm_1")]
    [InlineData("user", "", "pm_1")]
    [InlineData("user", " ", "pm_1")]
    [InlineData("user", "Stripe", "")]
    [InlineData("user", "Stripe", "  ")]
    public void A_method_missing_its_user_provider_or_token_is_refused(string user, string provider, string token) =>
        Assert.Throws<PaymentDomainException>(() => PaymentMethod.Create(user, provider, token));

    [Fact]
    public void A_new_method_carries_what_it_was_given_and_is_not_default()
    {
        var method = PaymentMethod.Create("user", "Stripe", "pm_1");

        Assert.NotEqual(Guid.Empty, method.Id);
        Assert.Equal("user", method.UserId);
        Assert.Equal("Stripe", method.PspName);
        Assert.Equal("pm_1", method.PspToken);
        Assert.False(method.IsDefault);
    }

    [Fact]
    public void A_method_marked_default_is_default_until_cleared()
    {
        var method = PaymentMethod.Create("user", "Stripe", "pm_1");

        method.MarkAsDefault();
        Assert.True(method.IsDefault);

        method.ClearDefault();
        Assert.False(method.IsDefault);
    }
}
