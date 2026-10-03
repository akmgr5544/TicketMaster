using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Shared;

namespace PaymentSystem.Data.Configurations;

internal sealed class PaymentOrderConfiguration : IEntityTypeConfiguration<PaymentOrder>
{
    public void Configure(EntityTypeBuilder<PaymentOrder> builder)
    {
        // The refunded amount can never pass what was paid; the domain refuses it, and this refuses any other writer.
        builder.ToTable("PaymentOrders", table => table.HasCheckConstraint(
            "CK_PaymentOrders_RefundedAmount", "\"RefundedAmount\" >= 0 AND \"RefundedAmount\" <= \"Amount\""));
        builder.HasKey(order => order.PaymentOrderId);
        builder.Property(order => order.PaymentOrderId).ValueGeneratedNever();

        // Backs the domain's one-order-per-seller rule for any write that does not go through the root.
        builder.HasIndex(order => new { order.CheckoutId, order.MerchantId }).IsUnique();

        builder.Property(order => order.BuyerId).IsRequired();
        builder.Property(order => order.MerchantId).IsRequired();
        builder.Property(order => order.Amount).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale).IsRequired();
        builder.Property(order => order.Currency).HasMaxLength(3).IsRequired();
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(order => order.Provider).HasMaxLength(PaymentOrder.ProviderMaxLength);
        builder.Property(order => order.PspToken).HasMaxLength(PaymentOrder.PspTokenMaxLength);
        builder.Property(order => order.RefundedAmount).HasPrecision(MoneyAmount.Precision, MoneyAmount.Scale).IsRequired();
        builder.Property(order => order.WalletUpdated).IsRequired();
        builder.Property(order => order.LedgerUpdated).IsRequired();
        builder.Property(order => order.CreatedAt).IsRequired();
        builder.Property(order => order.UpdatedAt).IsRequired();
        builder.HasMany(order => order.Refunds)
            .WithOne()
            .HasForeignKey(refund => refund.PaymentOrderId)
            .OnDelete(DeleteBehavior.Restrict);
        // Loaded with the order, as the orders are with the checkout: a repeated refund id is recognised only if the
        // order's earlier parts are there to compare against.
        builder.Navigation(order => order.Refunds)
            .HasField("_refunds")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
    }
}
