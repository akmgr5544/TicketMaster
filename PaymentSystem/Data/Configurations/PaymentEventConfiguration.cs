using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain;

namespace PaymentSystem.Data.Configurations;

internal sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        // Orders are added after the checkout is created, so nothing in the domain stops one being saved empty.
        builder.ToTable("PaymentEvents", table =>
            table.HasCheckConstraint("CK_PaymentEvents_OrderCount", "\"OrderCount\" > 0"));
        builder.HasKey(paymentEvent => paymentEvent.CheckoutId);
        builder.Property(paymentEvent => paymentEvent.CheckoutId).ValueGeneratedNever();
        builder.Ignore(paymentEvent => paymentEvent.DomainEvents);

        builder.HasIndex(paymentEvent => paymentEvent.BookingId).IsUnique();
        builder.Property(paymentEvent => paymentEvent.BuyerId).IsRequired();
        builder.Property(paymentEvent => paymentEvent.IsPaymentDone).IsRequired();
        builder.Property(paymentEvent => paymentEvent.OrderCount).IsRequired();
        builder.Property(paymentEvent => paymentEvent.Version).IsConcurrencyToken();
        builder.Property(paymentEvent => paymentEvent.CreatedAt).IsRequired();
        builder.Property(paymentEvent => paymentEvent.UpdatedAt).IsRequired();

        builder.HasMany(paymentEvent => paymentEvent.PaymentOrders)
            .WithOne()
            .HasForeignKey(order => order.CheckoutId)
            .OnDelete(DeleteBehavior.Restrict);
        // The root is only ever handled whole: loading a checkout without its orders would let the root
        // decide "all orders succeeded" over an empty list.
        builder.Navigation(paymentEvent => paymentEvent.PaymentOrders)
            .HasField("_paymentOrders")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .AutoInclude();
    }
}
