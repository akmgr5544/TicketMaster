using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Shared;

namespace PaymentSystem.Data.Configurations.Domain;

internal sealed class RefundOrderConfiguration : IEntityTypeConfiguration<RefundOrder>
{
    public void Configure(EntityTypeBuilder<RefundOrder> builder)
    {
        builder.ToTable("RefundOrders");
        builder.HasKey(refund => refund.RefundOrderId);
        builder.Property(refund => refund.RefundOrderId).ValueGeneratedNever();

        builder.ComplexProperty(refund => refund.RefundAmount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("RefundAmount").HasPrecision(Money.Precision, Money.Scale);
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3);
        });
        builder.Property(refund => refund.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(refund => refund.RefundReason).HasMaxLength(500).IsRequired();
        builder.Property(refund => refund.PspRefundReference).HasMaxLength(255);

        // A webhook and the refund reconciler can record the same answer at once; the second conflicts, and its
        // retry finds the refund already answered instead of moving the money twice.
        builder.Property<uint>("xmin").IsRowVersion();

        // PaymentOrder is owned by PaymentEvent, so no other aggregate can hold a foreign key to it. The index is
        // unique with the request id, so a redelivered refund request cannot refund the same order twice.
        builder.HasIndex(refund => new { refund.PaymentOrderId, refund.RequestId }).IsUnique();
        // How a PSP's refund event finds its refund when it carries no request id.
        builder.HasIndex(refund => refund.PspRefundReference);
    }
}
