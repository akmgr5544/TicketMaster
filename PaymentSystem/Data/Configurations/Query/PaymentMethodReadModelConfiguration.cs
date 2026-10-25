using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSystem.Query;

namespace PaymentSystem.Data.Configurations.Query;

internal sealed class PaymentMethodReadModelConfiguration : IEntityTypeConfiguration<PaymentMethodReadModel>
{
    public void Configure(EntityTypeBuilder<PaymentMethodReadModel> builder)
    {
        builder.ToTable("PaymentMethods", table => table.ExcludeFromMigrations());
        builder.HasKey(method => method.Id);
    }
}
