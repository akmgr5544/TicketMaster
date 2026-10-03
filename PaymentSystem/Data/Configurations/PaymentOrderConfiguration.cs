using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Shared;

namespace PaymentSystem.Data.Configurations;

internal sealed class PaymentOrderConfiguration : IEntityTypeConfiguration<PaymentOrder>
{
    public void Configure(EntityTypeBuilder<PaymentOrder> builder)
    {
        builder.ToTable("PaymentOrders");
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
        builder.Property(order => order.RefundReference).HasMaxLength(PaymentOrder.RefundReferenceMaxLength);
        builder.Property(order => order.WalletUpdated).IsRequired();
        builder.Property(order => order.LedgerUpdated).IsRequired();
        builder.Property(order => order.CreatedAt).IsRequired();
        builder.Property(order => order.UpdatedAt).IsRequired();
    }
}
