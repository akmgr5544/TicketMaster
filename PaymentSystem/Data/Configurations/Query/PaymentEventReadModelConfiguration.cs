using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Query;

namespace PaymentSystem.Data.Configurations.Query;

internal sealed class PaymentEventReadModelConfiguration : IEntityTypeConfiguration<PaymentEventReadModel>
{
    public void Configure(EntityTypeBuilder<PaymentEventReadModel> builder)
    {
        // The schema belongs to PaymentDbContext; this only reads it.
        builder.ToTable("PaymentEvents", table => table.ExcludeFromMigrations());
        builder.HasKey(paymentEvent => paymentEvent.CheckoutId);

        builder.HasMany(paymentEvent => paymentEvent.PaymentOrders)
            .WithOne()
            .HasForeignKey(order => order.CheckoutId);
    }
}
