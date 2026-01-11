using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Users.Api.Entities;

namespace Users.Api.Database.Configurations;

internal class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedOnAdd();
        
        builder.Property(user => user.Email).HasMaxLength(60).IsRequired();
        builder.HasIndex(user => user.Email).IsUnique();
        
        builder.Property(user => user.UserName).HasMaxLength(120).IsRequired();
        builder.HasIndex(user => user.UserName).IsUnique();
        
        builder.Property(user => user.PasswordHash).HasMaxLength(60).IsRequired();
        builder.Property(user => user.FirstName).HasMaxLength(120).IsRequired();
        builder.Property(user => user.LastName).HasMaxLength(120).IsRequired();
        builder.Property(user => user.PhoneNumber).HasMaxLength(120);
        builder.Property(user => user.RefreshToken).HasMaxLength(120);
    }
}