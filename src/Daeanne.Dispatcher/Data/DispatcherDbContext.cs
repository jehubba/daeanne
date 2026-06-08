using Daeanne.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Daeanne.Dispatcher.Data;

public class DispatcherDbContext(DbContextOptions<DispatcherDbContext> options) : DbContext(options)
{
    public DbSet<AgentTask>    Tasks         { get; set; }
    public DbSet<OutboxEmail>  OutboxEmails  { get; set; }
    public DbSet<OutboxSms>    OutboxSmsList { get; set; }
    public DbSet<ScheduledJob> ScheduledJobs { get; set; }

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

        modelBuilder.Entity<OutboxSms>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Status).HasConversion<string>();
            e.HasIndex(t => t.Status);
        });

        modelBuilder.Entity<ScheduledJob>(e =>
        {
            e.HasKey(j => j.Id);
            e.Property(j => j.JobType).HasConversion<string>();
            e.Property(j => j.TaskType).HasConversion<string>();
            e.HasIndex(j => j.NextRunAt);
            e.HasIndex(j => j.IsActive);
        });
    }
}
