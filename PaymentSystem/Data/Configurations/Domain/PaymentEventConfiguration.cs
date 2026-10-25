using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Domain;
using PaymentSystem.Domain.Shared;

namespace PaymentSystem.Data.Configurations.Domain;

internal sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        builder.ToTable("PaymentEvents");
        builder.HasKey(paymentEvent => paymentEvent.CheckoutId);
        builder.Property(paymentEvent => paymentEvent.CheckoutId).ValueGeneratedNever();

        builder.ComplexProperty(paymentEvent => paymentEvent.TotalAmount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("TotalAmount").HasPrecision(Money.Precision, Money.Scale);
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3);
        });

        // Every change to the checkout or one of its orders bumps it (see PaymentEvent.Version), so two writers on one
        // checkout conflict instead of one overwriting the other; the message retry then decides over the winner's state.
        builder.Property(paymentEvent => paymentEvent.Version).IsConcurrencyToken();

        // One checkout per booking: a redelivered PaymentRequested cannot create a second one.
        builder.HasIndex(paymentEvent => paymentEvent.BookingId).IsUnique();
        builder.HasIndex(paymentEvent => paymentEvent.BuyerId);

        builder.HasOne<PaymentMethod>()
            .WithMany()
            .HasForeignKey(paymentEvent => paymentEvent.PaymentMethodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.OwnsMany(paymentEvent => paymentEvent.PaymentOrders, orders =>
        {
            orders.ToTable("PaymentOrders");
            orders.WithOwner().HasForeignKey(order => order.CheckoutId);
            orders.HasKey(order => order.PaymentOrderId);
            orders.Property(order => order.PaymentOrderId).ValueGeneratedNever();

            // An owned type cannot hold a complex property, so Money is owned here too, in the same table.
            orders.OwnsOne(order => order.Amount, money =>
            {
                money.Property(m => m.Amount).HasColumnName("Amount").HasPrecision(Money.Precision, Money.Scale);
                money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3);
            });

            orders.Property(order => order.Status).HasConversion<string>().HasMaxLength(20);
            orders.Property(order => order.PspTokenRef).HasMaxLength(255);
            orders.Property(order => order.Provider).HasMaxLength(20);

            // Backs the domain's one-order-per-seller rule against any writer that skips the root.
            orders.HasIndex(order => new { order.CheckoutId, order.SellerId }).IsUnique();
            orders.HasIndex(order => order.SellerId);
            // Reconciliation's query: orders still executing, oldest first.
            orders.HasIndex(order => new { order.Status, order.ExecutingSince });
        });
    }
}
