using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PaymentSystem.Data.Interceptors;

// Matched by property name rather than an interface so the domain carries no persistence contract, and
// entities with only one of the two (Wallet, LedgerEntry) are stamped too.
internal sealed class AuditTimestampsInterceptor(TimeProvider timeProvider) : SaveChangesInterceptor
{
    private const string CreatedAt = "CreatedAt";
    private const string UpdatedAt = "UpdatedAt";

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        if (context is null)
            return;

        var now = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    SetIfMapped(entry, CreatedAt, now);
                    SetIfMapped(entry, UpdatedAt, now);
                    break;

                case EntityState.Modified:
                    SetIfMapped(entry, UpdatedAt, now);
                    // A disconnected Update marks every column modified; never let that rewrite the original.
                    if (entry.Metadata.FindProperty(CreatedAt) is not null)
                        entry.Property(CreatedAt).IsModified = false;
                    break;
            }
        }
    }

    private static void SetIfMapped(EntityEntry entry, string propertyName, DateTime value)
    {
        if (entry.Metadata.FindProperty(propertyName) is not null)
            entry.Property(propertyName).CurrentValue = value;
    }
}
