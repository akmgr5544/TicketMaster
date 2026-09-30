using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Shared.Pipelines;
using PaymentSystem.Shared.Results;

namespace PaymentIntegration.Fixtures;

public abstract class MessagingTest(PaymentsFixture fixture) : IntegrationTest(fixture), IAsyncLifetime
{
    protected IntegrationEventLog Outbox => fixture.Services.GetRequiredService<IntegrationEventLog>();

    // Re-implemented so xUnit calls this one; the log is the fixture's singleton and outlives each test.
    public new async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Outbox.Reset();
    }

    protected async Task<Result<T>> SendAsync<T>(IRequest<Result<T>> command)
    {
        await using var scope = NewScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(command);
    }

    // The webhook's shape without its slice: load the checkout, change an order through the root and save,
    // run by the production TransactionBehavior exactly as it runs a transactional command.
    protected async Task ThroughTransactionBehaviorAsync(Guid checkoutId, Action<PaymentEvent> change)
    {
        await using var scope = NewScope();
        var context = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();

        var behavior = new TransactionBehavior<WebhookLikeCommand, Result>(context);
        var result = await behavior.Handle(new WebhookLikeCommand(), async cancellationToken =>
        {
            LastTransactionId = context.Database.CurrentTransaction!.TransactionId;
            change(await context.PaymentEvents.SingleAsync(e => e.CheckoutId == checkoutId, cancellationToken));
            await context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    // The transaction the last ThroughTransactionBehaviorAsync opened, readable even when it threw.
    protected Guid LastTransactionId { get; private set; }

    protected Task<Wallet?> ReadWalletAsync(Guid ownerId, string currency = "USD") =>
        ReadAsync(c => c.Wallets.SingleOrDefaultAsync(w => w.OwnerId == ownerId && w.Currency == currency));

    protected Task<LedgerEntry[]> ReadLedgerAsync(Guid paymentOrderId) =>
        ReadAsync(c => c.LedgerEntries.Where(e => e.PaymentOrderId == paymentOrderId).ToArrayAsync());

    protected sealed record WebhookLikeCommand : IRequest<Result>, ITransactionalRequest;
}
