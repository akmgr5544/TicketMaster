using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data.Configurations.Query;
using PaymentSystem.Query;

namespace PaymentSystem.Data;

public class PaymentQueryDbContext(DbContextOptions<PaymentQueryDbContext> options) : DbContext(options)
{
    public DbSet<PaymentEventReadModel> PaymentEvents => Set<PaymentEventReadModel>();
    public DbSet<PaymentOrderReadModel> PaymentOrders => Set<PaymentOrderReadModel>();
    public DbSet<RefundOrderReadModel> RefundOrders => Set<RefundOrderReadModel>();
    public DbSet<PaymentMethodReadModel> PaymentMethods => Set<PaymentMethodReadModel>();
    public DbSet<WalletReadModel> Wallets => Set<WalletReadModel>();
    public DbSet<LedgerEntryReadModel> LedgerEntries => Set<LedgerEntryReadModel>();

    // Set here rather than at registration, so every instance reads without tracking however it was created.
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PaymentEventReadModelConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentOrderReadModelConfiguration());
        modelBuilder.ApplyConfiguration(new RefundOrderReadModelConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentMethodReadModelConfiguration());
        modelBuilder.ApplyConfiguration(new WalletReadModelConfiguration());
        modelBuilder.ApplyConfiguration(new LedgerEntryReadModelConfiguration());
    }
}
