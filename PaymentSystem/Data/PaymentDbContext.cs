using Microsoft.EntityFrameworkCore;
using PaymentSystem.Data.Configurations;
using PaymentSystem.Domain;

namespace PaymentSystem.Data;

public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<BookingClaim> BookingClaims => Set<BookingClaim>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new PaymentEventConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentOrderConfiguration());
        modelBuilder.ApplyConfiguration(new OrderRefundConfiguration());
        modelBuilder.ApplyConfiguration(new WalletConfiguration());
        modelBuilder.ApplyConfiguration(new LedgerEntryConfiguration());
        modelBuilder.ApplyConfiguration(new BookingClaimConfiguration());
    }
}
