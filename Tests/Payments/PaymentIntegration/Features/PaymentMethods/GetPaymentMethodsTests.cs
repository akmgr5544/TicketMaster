using PaymentIntegration.Fixtures;
using PaymentSystem.Features.PaymentMethods;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Features.PaymentMethods;

public sealed class GetPaymentMethodsTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task A_user_lists_only_their_own_methods_default_first()
    {
        var user = Guid.NewGuid();
        var other = await SeedPaymentMethodAsync(user, pspToken: "pm_other");
        var defaultMethod = await SeedPaymentMethodAsync(user, isDefault: true, pspToken: "pm_default");
        await SeedPaymentMethodAsync(Guid.NewGuid(), isDefault: true);

        var methods = await Sender.Send(new GetMyPaymentMethods.Query(user));

        Assert.Equal([defaultMethod.Id, other.Id], methods.Select(m => m.Id));
    }

    [Fact]
    public async Task The_owner_gets_one_method()
    {
        var user = Guid.NewGuid();
        var method = await SeedPaymentMethodAsync(user, isDefault: true, pspName: "Braintree");

        var result = await Sender.Send(new GetPaymentMethod.Query(user, method.Id));

        Assert.Equal(new GetPaymentMethod.Response(method.Id, "Braintree", true), result.Value);
    }

    [Fact]
    public async Task Someone_elses_method_is_not_found()
    {
        var method = await SeedPaymentMethodAsync(Guid.NewGuid());

        var result = await Sender.Send(new GetPaymentMethod.Query(Guid.NewGuid(), method.Id));

        Assert.Equal(ErrorType.NotFound, result.Error!.Type);
    }

    [Theory]
    [InlineData(typeof(GetMyPaymentMethods.Response))]
    [InlineData(typeof(GetPaymentMethod.Response))]
    [InlineData(typeof(AddPaymentMethod.Response))]
    public void No_response_carries_the_psp_token(Type response)
    {
        Assert.DoesNotContain(response.GetProperties(), p => p.Name.Contains("Token"));
    }
}
