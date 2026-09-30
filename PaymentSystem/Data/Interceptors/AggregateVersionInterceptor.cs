using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PaymentSystem.Domain;

namespace PaymentSystem.Data.Interceptors;

// Bumps PaymentEvent.Version at save time from the version the checkout was loaded with, never from a value
// the domain moved in memory. A domain-side bump broke the disconnected path: Update() treats the current
// value as the original, so a stale copy that had changed as often as the other writer matched the row and
// overwrote it, while an up-to-date copy was refused. Here the WHERE clause always carries the loaded version.
internal sealed class AggregateVersionInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        BumpChangedCheckouts(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        BumpChangedCheckouts(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private static void BumpChangedCheckouts(DbContext? context)
    {
        if (context is null)
            return;

        // An order-only change never writes the checkout row, so changed checkouts are found through their
        // orders as well as directly.
        var checkoutsWithChangedOrders = context.ChangeTracker.Entries<PaymentOrder>()
            .Where(entry => entry.State is EntityState.Modified or EntityState.Added)
            .Select(entry => entry.Entity.CheckoutId)
            .ToHashSet();

        foreach (var entry in context.ChangeTracker.Entries<PaymentEvent>())
        {
            // An added checkout is inserted at its initial version; there is nothing yet to conflict with.
            var changed = entry.State == EntityState.Modified
                          || (entry.State == EntityState.Unchanged
                              && checkoutsWithChangedOrders.Contains(entry.Entity.CheckoutId));
            if (!changed)
                continue;

            var version = entry.Property(paymentEvent => paymentEvent.Version);
            version.CurrentValue = version.OriginalValue + 1;
        }
    }
}
