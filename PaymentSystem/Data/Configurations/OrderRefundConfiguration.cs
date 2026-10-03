using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Shared;

namespace PaymentSystem.Data.Configurations;

internal sealed class OrderRefundConfiguration : IEntityTypeConfiguration<OrderRefund>
{
    public void Configure(EntityTypeBuilder<OrderRefund> builder)
    {
        builder.ToTable("OrderRefunds");
        // Per order, not global: a whole-checkout refund gives every paid order a part under the same id.
        builder.HasKey(refund => new { refund.PaymentOrderId, refund.RefundId });

        builder.Property(refund => refund.Amount).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale).IsRequired();
        builder.Property(refund => refund.ProviderReference).HasMaxLength(OrderRefund.ProviderReferenceMaxLength).IsRequired();
        builder.Property(refund => refund.CreatedAt).IsRequired();
    }
}
