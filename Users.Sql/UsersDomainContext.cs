using Microsoft.EntityFrameworkCore;
using Users.Domain.Entities;
using Users.Sql.Configurations;

namespace Users.Sql;

public class UsersDomainContext : DbContext
{
    public DbSet<User>  Users { get; set; }
    
    public UsersDomainContext(DbContextOptions options) : base(options)
    {
        
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new UserConfiguration());
    }
}