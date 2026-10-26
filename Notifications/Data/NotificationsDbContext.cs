using Microsoft.EntityFrameworkCore;
using Notifications.Domain;

namespace Notifications.Data;

public class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<BookingBuyer> BookingBuyers => Set<BookingBuyer>();
    public DbSet<Recipient> Recipients => Set<Recipient>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BookingBuyer>(builder =>
        {
            builder.ToTable("BookingBuyers");
            builder.HasKey(buyer => buyer.BookingId);
            builder.Property(buyer => buyer.BookingId).ValueGeneratedNever();
        });

        modelBuilder.Entity<Recipient>(builder =>
        {
            builder.ToTable("Recipients");
            builder.HasKey(recipient => recipient.UserId);
            builder.Property(recipient => recipient.UserId).ValueGeneratedNever();
            builder.Property(recipient => recipient.Email).HasMaxLength(256).IsRequired();
        });

        modelBuilder.Entity<Notification>(builder =>
        {
            builder.ToTable("Notifications");
            builder.HasKey(notification => notification.NotificationId);
            builder.Property(notification => notification.NotificationId).ValueGeneratedNever();
            builder.Property(notification => notification.Kind).HasConversion<string>().HasMaxLength(30);
            builder.Property(notification => notification.Recipient).HasMaxLength(256).IsRequired();
            // One email per outcome: two copies of one message racing to insert lose here, and the retry finds it.
            builder.HasIndex(notification => new { notification.BookingId, notification.Kind, notification.RefundId }).IsUnique();
            // A voided booking's cancellation and payment failure are one email: when both arrive at once, the
            // handler's read-first check passes for both, so this index is what makes the second lose and retry.
            builder.HasIndex(notification => notification.BookingId)
                .IsUnique()
                .HasFilter($"\"Kind\" IN ('{NotificationKind.PaymentFailed}', '{NotificationKind.BookingCancelled}')")
                .HasDatabaseName("IX_Notifications_BookingId_Voided");
        });
    }
}
