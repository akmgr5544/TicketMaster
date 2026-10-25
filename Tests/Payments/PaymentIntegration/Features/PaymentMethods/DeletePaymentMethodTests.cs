using Microsoft.EntityFrameworkCore;
using PaymentIntegration.Fixtures;
using PaymentSystem.Features.PaymentMethods;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.PaymentMethods;

public sealed class DeletePaymentMethodTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task The_owner_removes_their_method()
    {
        var user = Guid.NewGuid();
        var method = await SeedPaymentMethodAsync(user);

        var result = await Sender.Send(new DeletePaymentMethod.Command(user, method.Id));

        Assert.True(result.IsSuccess);
        Assert.False(await ReadAsync(c => c.PaymentMethods.AnyAsync(m => m.Id == method.Id)));
    }

    [Fact]
    public async Task Someone_elses_method_is_not_found_and_stays()
    {
        var method = await SeedPaymentMethodAsync(Guid.NewGuid());

        var result = await Sender.Send(new DeletePaymentMethod.Command(Guid.NewGuid(), method.Id));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
        Assert.True(await ReadAsync(c => c.PaymentMethods.AnyAsync(m => m.Id == method.Id)));
    }

    [Fact]
    public async Task A_method_a_checkout_used_cannot_be_removed()
    {
        var user = Guid.NewGuid();
        var method = await SeedPaymentMethodAsync(user);
        await SeedCheckoutAsync(buyerId: user, paymentMethodId: method.Id);

        var result = await Sender.Send(new DeletePaymentMethod.Command(user, method.Id));

        Assert.Equal(ErrorType.Conflict, result.Error!.Type);
        Assert.Equal("payment_method_in_use", result.Error.Code);
        Assert.True(await ReadAsync(c => c.PaymentMethods.AnyAsync(m => m.Id == method.Id)));
    }
}
