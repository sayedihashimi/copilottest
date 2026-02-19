using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ProcWatch.MonitorService.Data;

public class ProcWatchDbContext : DbContext
{
    public ProcWatchDbContext(DbContextOptions<ProcWatchDbContext> options)
        : base(options)
    {
    }

    public DbSet<MonitoredSession> MonitoredSessions => Set<MonitoredSession>();
    public DbSet<ProcessInstance> ProcessInstances => Set<ProcessInstance>();
    public DbSet<EventRecord> EventRecords => Set<EventRecord>();
    public DbSet<StatsSample> StatsSamples => Set<StatsSample>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // MonitoredSession
        modelBuilder.Entity<MonitoredSession>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StartTime);
            entity.HasIndex(e => e.TargetPid);
            
            entity.HasMany(e => e.ProcessInstances)
                .WithOne(e => e.Session)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasMany(e => e.EventRecords)
                .WithOne(e => e.Session)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasMany(e => e.StatsSamples)
                .WithOne(e => e.Session)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ProcessInstance
        modelBuilder.Entity<ProcessInstance>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Pid);
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => new { e.SessionId, e.Pid });
            
            entity.HasMany(e => e.EventRecords)
                .WithOne(e => e.ProcessInstance)
                .HasForeignKey(e => e.ProcessInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
            
            entity.HasMany(e => e.StatsSamples)
                .WithOne(e => e.ProcessInstance)
                .HasForeignKey(e => e.ProcessInstanceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // EventRecord
        modelBuilder.Entity<EventRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.EventType);
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.ProcessInstanceId);
        });

        // StatsSample
        modelBuilder.Entity<StatsSample>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Timestamp);
            entity.HasIndex(e => e.SessionId);
            entity.HasIndex(e => e.ProcessInstanceId);
        });
    }
}

/// <summary>
/// Design-time factory for EF Core migrations
/// </summary>
public class ProcWatchDbContextFactory : IDesignTimeDbContextFactory<ProcWatchDbContext>
{
    public ProcWatchDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProcWatchDbContext>();
        optionsBuilder.UseSqlite("Data Source=procwatch_design.db");
        return new ProcWatchDbContext(optionsBuilder.Options);
    }
}
