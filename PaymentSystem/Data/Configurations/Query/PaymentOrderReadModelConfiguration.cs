using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Query;

namespace PaymentSystem.Data.Configurations.Query;

internal sealed class PaymentOrderReadModelConfiguration : IEntityTypeConfiguration<PaymentOrderReadModel>
{
    public void Configure(EntityTypeBuilder<PaymentOrderReadModel> builder)
    {
        builder.ToTable("PaymentOrders", table => table.ExcludeFromMigrations());
        builder.HasKey(order => order.PaymentOrderId);
    }
}
