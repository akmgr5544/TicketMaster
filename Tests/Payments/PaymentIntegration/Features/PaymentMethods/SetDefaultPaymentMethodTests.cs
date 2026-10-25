using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Features.PaymentMethods;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.PaymentMethods;

public sealed class SetDefaultPaymentMethodTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task Making_a_method_default_moves_the_default_to_it()
    {
        var user = Guid.NewGuid();
        var old = await SeedPaymentMethodAsync(user, isDefault: true);
        var chosen = await SeedPaymentMethodAsync(user, pspToken: "pm_2");

        var result = await Sender.Send(new SetDefaultPaymentMethod.Command(user, chosen.Id));

        Assert.True(result.IsSuccess);
        var methods = await ReadAsync(c => c.PaymentMethods.Where(m => m.UserId == user).ToListAsync());
        Assert.Equal(chosen.Id, Assert.Single(methods, m => m.IsDefault).Id);
        Assert.False(methods.Single(m => m.Id == old.Id).IsDefault);
    }

    [Fact]
    public async Task Making_the_default_default_again_succeeds_and_changes_nothing()
    {
        var user = Guid.NewGuid();
        var method = await SeedPaymentMethodAsync(user, isDefault: true);

        var result = await Sender.Send(new SetDefaultPaymentMethod.Command(user, method.Id));

        Assert.True(result.IsSuccess);
        Assert.True((await ReadAsync(c => c.PaymentMethods.SingleAsync(m => m.Id == method.Id))).IsDefault);
    }

    [Fact]
    public async Task Someone_elses_method_is_not_found()
    {
        var owner = Guid.NewGuid();
        var method = await SeedPaymentMethodAsync(owner);

        var result = await Sender.Send(new SetDefaultPaymentMethod.Command(Guid.NewGuid(), method.Id));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.False((await ReadAsync(c => c.PaymentMethods.SingleAsync(m => m.Id == method.Id))).IsDefault);
    }
}
