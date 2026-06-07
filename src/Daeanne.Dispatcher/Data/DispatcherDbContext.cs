using Daeanne.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Daeanne.Dispatcher.Data;

public class DispatcherDbContext(DbContextOptions<DispatcherDbContext> options) : DbContext(options)
{
    public DbSet<AgentTask> Tasks { get; set; }
    public DbSet<OutboxEmail> OutboxEmails { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AgentTask>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Type).HasConversion<string>();
            e.Property(t => t.Status).HasConversion<string>();
            e.HasIndex(t => t.Status);
            e.HasIndex(t => t.CreatedAt);
            e.HasIndex(t => t.CorrelationId);
        });

        modelBuilder.Entity<OutboxEmail>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Status).HasConversion<string>();
            e.HasIndex(t => t.Status);
        });
    }
}
