using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentIntegration.Fixtures;
using PaymentSystem.Data;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Events;
using PaymentSystem.Enums;

namespace PaymentIntegration.DomainEvents;

public sealed class DomainEventDispatchTests(PaymentsFixture fixture) : IntegrationTest(fixture)
{
    [Fact]
    public async Task SucceedOrder_OnSave_PublishesSucceededEventOnceWithTheRightIds()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);
        var checkout = await LoadCheckoutAsync(saved.CheckoutId);

        checkout.SucceedOrder(saved.OrderId(0));
        await Context.SaveChangesAsync();

        var succeeded = Assert.IsType<PaymentOrderSucceededDomainEvent>(Assert.Single(Events.Published).Event);
        Assert.Equal(saved.OrderId(0), succeeded.PaymentOrderId);
        Assert.Equal(saved.CheckoutId, succeeded.CheckoutId);
        Assert.Empty(checkout.DomainEvents);
    }

    [Fact]
    public async Task FailOrder_OnSave_PublishesFailedEventOnceWithTheRightIds()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);

        (await LoadCheckoutAsync(saved.CheckoutId)).FailOrder(saved.OrderId(0));
        await Context.SaveChangesAsync();

        var failed = Assert.IsType<PaymentOrderFailedDomainEvent>(Assert.Single(Events.Published).Event);
        Assert.Equal(saved.OrderId(0), failed.PaymentOrderId);
        Assert.Equal(saved.CheckoutId, failed.CheckoutId);
    }

    [Fact]
    public async Task SeveralOrdersSettledInOneSave_EachEventPublishedExactlyOnce()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing, OrderState.Executing, OrderState.Executing);
        var checkout = await LoadCheckoutAsync(saved.CheckoutId);

        checkout.SucceedOrder(saved.OrderId(0));
        checkout.FailOrder(saved.OrderId(1));
        checkout.SucceedOrder(saved.OrderId(2));
        await Context.SaveChangesAsync();

        Assert.Equal(3, Events.Published.Count);
        Assert.Single(Events.Published, p => p.Event is PaymentOrderSucceededDomainEvent e && e.PaymentOrderId == saved.OrderId(0));
        Assert.Single(Events.Published, p => p.Event is PaymentOrderFailedDomainEvent e && e.PaymentOrderId == saved.OrderId(1));
        Assert.Single(Events.Published, p => p.Event is PaymentOrderSucceededDomainEvent e && e.PaymentOrderId == saved.OrderId(2));
    }

    [Fact]
    public async Task NewCheckoutWithSettledOrders_PublishesOnInsert()
    {
        var checkout = CheckoutSeed.New(orders: 2);
        CheckoutSeed.Drive(checkout, checkout.OrderId(0), OrderState.Success);
        CheckoutSeed.Drive(checkout, checkout.OrderId(1), OrderState.Failed);
        Context.PaymentEvents.Add(checkout);
        await Context.SaveChangesAsync();

        Assert.Equal(2, Events.Published.Count);
    }

    [Fact]
    public async Task Publish_HappensAfterTheRowIsCommitted()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);

        (await LoadCheckoutAsync(saved.CheckoutId)).SucceedOrder(saved.OrderId(0));
        await Context.SaveChangesAsync();

        Assert.Equal(PaymentOrderStatus.Success, Assert.Single(Events.Published).StatusInDatabase);
    }

    [Fact]
    public async Task SecondSaveInSameScope_DoesNotRepublish()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);
        var checkout = await LoadCheckoutAsync(saved.CheckoutId);
        checkout.SucceedOrder(saved.OrderId(0));
        await Context.SaveChangesAsync();

        checkout.MarkWalletUpdated(saved.OrderId(0));
        await Context.SaveChangesAsync();
        await Context.SaveChangesAsync();

        Assert.Single(Events.Published);
    }

    [Fact]
    public async Task RedeliveredSucceed_RaisesAndPublishesNothing()
    {
        var saved = await SeedCheckoutAsync(OrderState.Success);
        var checkout = await LoadCheckoutAsync(saved.CheckoutId);

        checkout.SucceedOrder(saved.OrderId(0));
        Assert.Empty(checkout.DomainEvents);
        await Context.SaveChangesAsync();

        Assert.Empty(Events.Published);
    }

    [Fact]
    public async Task RedeliveredFail_RaisesAndPublishesNothing()
    {
        var saved = await SeedCheckoutAsync(OrderState.Failed);
        var checkout = await LoadCheckoutAsync(saved.CheckoutId);

        checkout.FailOrder(saved.OrderId(0));
        await Context.SaveChangesAsync();

        Assert.Empty(Events.Published);
    }

    [Fact]
    public async Task HandlerThatSaves_ReentersInterceptor_WithoutDuplicatePublication()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);
        var id = saved.OrderId(0);
        Events.OnPublish = async (services, _) =>
        {
            var context = services.GetRequiredService<PaymentDbContext>();
            (await context.PaymentEvents.SingleAsync(e => e.CheckoutId == saved.CheckoutId)).MarkLedgerUpdated(id);
            await context.SaveChangesAsync();
        };

        (await LoadCheckoutAsync(saved.CheckoutId)).SucceedOrder(id);
        await Context.SaveChangesAsync();

        Assert.Single(Events.Published);
        Assert.True((await ReadOrderAsync(id)).LedgerUpdated);
    }

    [Fact]
    public async Task HandlerThatSettlesASibling_PublishesTheNewEventOnce_AndCheckoutEndsDone()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing, OrderState.Executing);
        var first = saved.OrderId(0);
        var sibling = saved.OrderId(1);
        Events.OnPublish = async (services, domainEvent) =>
        {
            if (domainEvent is not PaymentOrderSucceededDomainEvent e || e.PaymentOrderId != first)
                return;
            var context = services.GetRequiredService<PaymentDbContext>();
            (await context.PaymentEvents.SingleAsync(x => x.CheckoutId == saved.CheckoutId)).SucceedOrder(sibling);
            await context.SaveChangesAsync();
        };

        (await LoadCheckoutAsync(saved.CheckoutId)).SucceedOrder(first);
        await Context.SaveChangesAsync();

        Assert.Equal(2, Events.Published.Count);
        Assert.Single(Events.Published, p => ((PaymentOrderSucceededDomainEvent)p.Event).PaymentOrderId == first);
        Assert.Single(Events.Published, p => ((PaymentOrderSucceededDomainEvent)p.Event).PaymentOrderId == sibling);
        Assert.True((await ReadCheckoutAsync(saved.CheckoutId)).IsPaymentDone);
    }

    [Fact]
    public async Task HandlerThrows_WithoutOuterTransaction_RowStaysCommittedAndEventIsGone()
    {
        // Documents observed behaviour, not a rule: the write commits before publication and the events
        // are cleared before the handler runs, so a failing handler leaves a committed row whose side effect
        // never happened and will never be retried from this aggregate.
        var saved = await SeedCheckoutAsync(OrderState.Executing);
        var checkout = await LoadCheckoutAsync(saved.CheckoutId);
        Events.OnPublish = (_, _) => throw new InvalidOperationException("handler failed");

        checkout.SucceedOrder(saved.OrderId(0));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Context.SaveChangesAsync());

        Assert.Equal(PaymentOrderStatus.Success, (await ReadOrderAsync(saved.OrderId(0))).Status);
        Assert.Empty(checkout.DomainEvents);
    }

    [Fact]
    public async Task HandlerThrows_InsideCallerTransaction_RollbackUndoesRowAndHandlerWrite()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);
        var id = saved.OrderId(0);
        var checkout = await LoadCheckoutAsync(saved.CheckoutId);
        Events.OnPublish = async (services, _) =>
        {
            var context = services.GetRequiredService<PaymentDbContext>();
            (await context.PaymentEvents.SingleAsync(e => e.CheckoutId == saved.CheckoutId)).MarkWalletUpdated(id);
            await context.SaveChangesAsync();
            throw new InvalidOperationException("handler failed after its own save");
        };

        await using (var transaction = await Context.Database.BeginTransactionAsync())
        {
            checkout.SucceedOrder(id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Context.SaveChangesAsync());
            await transaction.RollbackAsync();
        }

        var stored = await ReadCheckoutAsync(saved.CheckoutId);
        Assert.Equal(PaymentOrderStatus.Executing, stored.Order(id).Status);
        Assert.False(stored.Order(id).WalletUpdated);
        Assert.False(stored.IsPaymentDone);
    }

    [Fact]
    public async Task InsideCallerTransaction_HandlerRunsBeforeCommit_OnTheCallersContext()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);

        await using (var transaction = await Context.Database.BeginTransactionAsync())
        {
            (await LoadCheckoutAsync(saved.CheckoutId)).SucceedOrder(saved.OrderId(0));
            await Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        var published = Assert.Single(Events.Published);
        // Another connection still saw the old state: the handler ran before commit.
        Assert.Equal(PaymentOrderStatus.Executing, published.StatusInDatabase);
        Assert.Same(Context, published.HandlerContext);
    }

    [Fact]
    public async Task FailedSave_PublishesNothing()
    {
        var existing = await SeedCheckoutAsync(OrderState.NotStarted);
        var clash = CheckoutSeed.Create(existing.CheckoutId, Random.Shared.NextInt64(1, long.MaxValue), Guid.NewGuid(), CheckoutSeed.Lines(1));
        CheckoutSeed.Drive(clash, clash.OrderId(0), OrderState.Success);
        Context.PaymentEvents.Add(clash);

        await Assert.ThrowsAsync<DbUpdateException>(() => Context.SaveChangesAsync());

        Assert.Empty(Events.Published);
    }

    [Fact]
    public async Task ConcurrencyLoser_PublishesNothing()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);
        var id = saved.OrderId(0);
        var loser = await LoadCheckoutAsync(saved.CheckoutId);
        await InScopeAsync(async c => { (await c.PaymentEvents.SingleAsync()).SucceedOrder(id); await c.SaveChangesAsync(); });
        Events.Reset();

        loser.FailOrder(id);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Context.SaveChangesAsync());

        Assert.Empty(Events.Published);
    }

    [Fact]
    public async Task SyncSaveChanges_WithPendingEvent_ThrowsBeforeWriting()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);
        var checkout = await LoadCheckoutAsync(saved.CheckoutId);
        checkout.SucceedOrder(saved.OrderId(0));

        Assert.Throws<NotSupportedException>(() => Context.SaveChanges());

        var stored = await ReadCheckoutAsync(saved.CheckoutId);
        Assert.Equal(PaymentOrderStatus.Executing, stored.Order(saved.OrderId(0)).Status);
        Assert.False(stored.IsPaymentDone);
        Assert.Single(checkout.DomainEvents);
        Assert.Empty(Events.Published);
    }

    [Fact]
    public async Task SyncSaveChanges_WithPendingEventOnNewCheckout_ThrowsAndInsertsNothing()
    {
        var checkout = CheckoutSeed.New();
        CheckoutSeed.Drive(checkout, checkout.OrderId(0), OrderState.Success);
        Context.PaymentEvents.Add(checkout);

        Assert.Throws<NotSupportedException>(() => Context.SaveChanges());

        Assert.Equal(0, await ReadAsync(c => c.PaymentEvents.CountAsync()));
        Assert.Equal(0, await ReadAsync(c => c.Set<PaymentOrder>().CountAsync()));
    }

    [Fact]
    public async Task SyncSaveChanges_WithNoPendingEvents_Succeeds()
    {
        var saved = await SeedCheckoutAsync(OrderState.Success);

        (await LoadCheckoutAsync(saved.CheckoutId)).MarkWalletUpdated(saved.OrderId(0));
        Context.SaveChanges();

        Assert.True((await ReadOrderAsync(saved.OrderId(0))).WalletUpdated);
    }

    [Fact]
    public async Task Handler_ResolvesTheSameContextInstanceAsTheScopeThatSaved()
    {
        var saved = await SeedCheckoutAsync(OrderState.Executing);

        (await LoadCheckoutAsync(saved.CheckoutId)).SucceedOrder(saved.OrderId(0));
        await Context.SaveChangesAsync();

        Assert.Same(Context, Assert.Single(Events.Published).HandlerContext);
    }

    [Fact]
    public async Task TwoScopes_EachHandlerGetsItsOwnScopesContext()
    {
        var a = await SeedCheckoutAsync(OrderState.Executing);
        var b = await SeedCheckoutAsync(OrderState.Executing);

        PaymentDbContext? contextA = null, contextB = null;
        await InScopeAsync(async c =>
        {
            contextA = c;
            (await c.PaymentEvents.SingleAsync(e => e.CheckoutId == a.CheckoutId)).SucceedOrder(a.OrderId(0));
            await c.SaveChangesAsync();
        });
        await InScopeAsync(async c =>
        {
            contextB = c;
            (await c.PaymentEvents.SingleAsync(e => e.CheckoutId == b.CheckoutId)).SucceedOrder(b.OrderId(0));
            await c.SaveChangesAsync();
        });

        Assert.Equal(2, Events.Published.Count);
        Assert.Same(contextA, Events.Published[0].HandlerContext);
        Assert.Same(contextB, Events.Published[1].HandlerContext);
    }
}
