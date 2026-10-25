using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Exceptions;
using PaymentSystem.Features.PaymentMethods;

namespace PaymentIntegration.Features.PaymentMethods;

public sealed class AddPaymentMethodTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    private Task<List<PaymentMethod>> ReadMethodsAsync(Guid userId) =>
        ReadAsync(c => c.PaymentMethods.Where(m => m.UserId == userId.ToString()).ToListAsync());

    [Fact]
    public async Task A_users_first_method_becomes_their_default_even_unasked()
    {
        var user = Guid.NewGuid();

        var result = await Sender.Send(new AddPaymentMethod.Command(user, "Stripe", "pm_1", MakeDefault: false));

        Assert.True(result.Value!.IsDefault);
        var stored = Assert.Single(await ReadMethodsAsync(user));
        Assert.True(stored.IsDefault);
        Assert.Equal("Stripe", stored.PspName);
        Assert.Equal("pm_1", stored.PspToken);
    }

    [Fact]
    public async Task A_later_method_is_not_the_default_unless_asked()
    {
        var user = Guid.NewGuid();
        var first = await SeedPaymentMethodAsync(user, isDefault: true);

        var result = await Sender.Send(new AddPaymentMethod.Command(user, "Stripe", "pm_2", MakeDefault: false));

        Assert.False(result.Value!.IsDefault);
        Assert.Equal(first.Id, Assert.Single(await ReadMethodsAsync(user), m => m.IsDefault).Id);
    }

    [Fact]
    public async Task Asking_for_the_new_method_to_be_default_moves_the_default_to_it()
    {
        var user = Guid.NewGuid();
        await SeedPaymentMethodAsync(user, isDefault: true);

        var result = await Sender.Send(new AddPaymentMethod.Command(user, "Braintree", "pm_2", MakeDefault: true));

        var methods = await ReadMethodsAsync(user);
        Assert.Equal(2, methods.Count);
        Assert.Equal(result.Value!.Id, Assert.Single(methods, m => m.IsDefault).Id);
    }

    [Fact]
    public async Task Another_users_default_is_untouched()
    {
        var other = Guid.NewGuid();
        var theirs = await SeedPaymentMethodAsync(other, isDefault: true);

        await Sender.Send(new AddPaymentMethod.Command(Guid.NewGuid(), "Stripe", "pm_mine", MakeDefault: true));

        Assert.True(Assert.Single(await ReadMethodsAsync(other)).IsDefault);
        Assert.Equal(theirs.Id, Assert.Single(await ReadMethodsAsync(other)).Id);
    }

    [Theory]
    [InlineData("Stripe", "")]
    [InlineData("", "pm_1")]
    [InlineData("Stripe", "   ")]
    public async Task A_method_without_a_provider_or_token_is_refused(string pspName, string pspToken)
    {
        var user = Guid.NewGuid();

        await Assert.ThrowsAsync<PaymentDomainException>(() =>
            Sender.Send(new AddPaymentMethod.Command(user, pspName, pspToken, MakeDefault: true)));

        Assert.Empty(await ReadMethodsAsync(user));
    }

    [Fact]
    public async Task The_database_refuses_a_second_default_for_one_user()
    {
        var user = Guid.NewGuid();
        await SeedPaymentMethodAsync(user, isDefault: true);

        await Assert.ThrowsAsync<DbUpdateException>(() => SeedPaymentMethodAsync(user, isDefault: true, pspToken: "pm_other"));
    }
}
