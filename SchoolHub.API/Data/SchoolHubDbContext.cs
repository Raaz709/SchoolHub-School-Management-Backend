using Microsoft.EntityFrameworkCore;
using SchoolHub.API.Models;

namespace SchoolHub.API.Data
{
    public class SchoolHubDbContext : DbContext
    {
        public SchoolHubDbContext(DbContextOptions<SchoolHubDbContext> options) : base(options) { }

        public DbSet<Tenant> Tenants => Set<Tenant>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<Tenant>().HasIndex(t => t.Identifier).IsUnique();
        }
    }
}
