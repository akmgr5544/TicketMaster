using Microsoft.EntityFrameworkCore;
using Users.Api.Database.Configurations;
using Users.Api.Entities;

namespace Users.Api.Database;

public class UsersDomainContext : DbContext
{
    public DbSet<User> Users { get; set; }

    public UsersDomainContext(DbContextOptions options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new UserConfiguration());
    }
}