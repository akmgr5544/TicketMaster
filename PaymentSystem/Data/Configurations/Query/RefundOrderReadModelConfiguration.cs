using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Query;

namespace PaymentSystem.Data.Configurations.Query;

internal sealed class RefundOrderReadModelConfiguration : IEntityTypeConfiguration<RefundOrderReadModel>
{
    public void Configure(EntityTypeBuilder<RefundOrderReadModel> builder)
    {
        builder.ToTable("RefundOrders", table => table.ExcludeFromMigrations());
        builder.HasKey(refund => refund.RefundOrderId);
    }
}
