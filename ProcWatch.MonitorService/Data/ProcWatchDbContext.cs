using Microsoft.EntityFrameworkCore;
using ProcWatch.MonitorService.Data.Entities;

namespace ProcWatch.MonitorService.Data;

public class ProcWatchDbContext : DbContext
{
    public ProcWatchDbContext(DbContextOptions<ProcWatchDbContext> options)
        : base(options)
    {
    }

    public DbSet<MonitoredSession> MonitoredSessions { get; set; }
    public DbSet<ProcessInstance> ProcessInstances { get; set; }
    public DbSet<EventRecord> EventRecords { get; set; }
    public DbSet<StatsSample> StatsSamples { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // MonitoredSession
        modelBuilder.Entity<MonitoredSession>(entity =>
        {
            entity.HasKey(e => e.SessionId);
            entity.Property(e => e.ProcessName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.ArgsJson).HasMaxLength(4000);
        });

        // ProcessInstance
        modelBuilder.Entity<ProcessInstance>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProcessName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.CommandLine).HasMaxLength(4000);
            
            entity.HasOne(e => e.Session)
                .WithMany(s => s.Processes)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
                
            entity.HasIndex(e => new { e.SessionId, e.Pid });
        });

        // EventRecord
        modelBuilder.Entity<EventRecord>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProcessName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Type).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Op).IsRequired().HasMaxLength(50);
            entity.Property(e => e.Path).HasMaxLength(2000);
            entity.Property(e => e.Endpoints).HasMaxLength(500);
            entity.Property(e => e.Source).HasMaxLength(100);
            
            entity.HasOne(e => e.Session)
                .WithMany(s => s.Events)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
                
            entity.HasIndex(e => new { e.SessionId, e.Timestamp });
            entity.HasIndex(e => new { e.SessionId, e.Type });
            entity.HasIndex(e => e.Path);
        });

        // StatsSample
        modelBuilder.Entity<StatsSample>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ProcessName).IsRequired().HasMaxLength(256);
            
            entity.HasOne(e => e.Session)
                .WithMany(s => s.StatsSamples)
                .HasForeignKey(e => e.SessionId)
                .OnDelete(DeleteBehavior.Cascade);
                
            entity.HasIndex(e => new { e.SessionId, e.Timestamp });
            entity.HasIndex(e => new { e.SessionId, e.Pid });
        });
    }
}
