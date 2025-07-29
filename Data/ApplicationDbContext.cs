using Microsoft.EntityFrameworkCore;
using OutSystemsMcpServer.Models;

namespace OutSystemsMcpServer.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<DeploymentPlan> DeploymentPlans { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<DeploymentPlan>()
            .HasIndex(dp => new { dp.PlanName, dp.DeployedTo })
            .IsUnique();
    }
}