using Microsoft.Extensions.DependencyInjection;
using Notifications.Data;
using Notifications.Domain;

namespace NotificationsIntegration.Fixtures;

[Collection(NotificationsCollection.Name)]
public abstract class IntegrationTest(NotificationsFixture fixture) : IAsyncLifetime
{
    protected RecordingEmailSender Emails => fixture.Emails;

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // Reading back through the scope that wrote proves nothing about what reached the database.
    protected async Task<T> ReadAsync<T>(Func<NotificationsDbContext, Task<T>> read)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<NotificationsDbContext>());
    }

    // Runs a Wolverine handler the way Wolverine's EF Core middleware does: built from the message's own scope, handed
    // that scope's context, and its changes saved once it returns. A [NonTransactional] handler saves for itself, and a
    // second save here finds nothing to write.
    protected async Task<TResult> HandleAsync<THandler, TResult>(
        Func<THandler, NotificationsDbContext, Task<TResult>> handle) where THandler : notnull
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var handler = ActivatorUtilities.CreateInstance<THandler>(scope.ServiceProvider);
        var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        var result = await handle(handler, context);
        await context.SaveChangesAsync();
        return result;
    }

    protected Task HandleAsync<THandler>(Func<THandler, NotificationsDbContext, Task> handle) where THandler : notnull =>
        HandleAsync<THandler, bool>(async (handler, context) =>
        {
            await handle(handler, context);
            return true;
        });

    protected async Task SeedAsync(params object[] rows)
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        context.AddRange(rows);
        await context.SaveChangesAsync();
    }

    protected static long NewBookingId() => Random.Shared.NextInt64(1, long.MaxValue);

    // A booking whose buyer has an address: the case every outcome email needs.
    protected async Task<(long BookingId, string Email)> SeedKnownBuyerAsync()
    {
        var bookingId = NewBookingId();
        var buyerId = Guid.NewGuid();
        var email = $"{buyerId:N}@example.com";
        await SeedAsync(new BookingBuyer(bookingId, buyerId), new Recipient(buyerId, email));
        return (bookingId, email);
    }
}
