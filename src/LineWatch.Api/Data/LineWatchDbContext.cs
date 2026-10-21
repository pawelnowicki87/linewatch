using LineWatch.Core;
using LineWatch.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LineWatch.Api.Data;

public sealed class LineWatchDbContext(DbContextOptions<LineWatchDbContext> options) : DbContext(options)
{
    public DbSet<StoredReading> Readings => Set<StoredReading>();
    public DbSet<Alarm> Alarms => Set<Alarm>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<StoredReading>(reading =>
        {
            reading.ToTable("readings");
            reading.Property(r => r.MachineId).HasMaxLength(64);
            reading.Property(r => r.State).HasConversion<string>().HasMaxLength(16);
            reading.HasIndex(r => new { r.MachineId, r.Timestamp });
        });

        modelBuilder.Entity<Alarm>(alarm =>
        {
            alarm.ToTable("alarms");
            alarm.Property(a => a.MachineId).HasMaxLength(64);
            alarm.Property(a => a.Kind).HasConversion<string>().HasMaxLength(32);
            alarm.Property(a => a.Message).HasMaxLength(200);
            alarm.HasIndex(a => a.RaisedAt);

            // At most one open alarm of each kind per machine, enforced by the database too.
            alarm.HasIndex(a => new { a.MachineId, a.Kind })
                .IsUnique()
                .HasFilter("\"ClearedAt\" IS NULL");
        });
    }
}

public sealed class StoredReading
{
    public long Id { get; set; }
    public required string MachineId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public MachineState State { get; set; }
    public double TemperatureC { get; set; }
    public int SpeedRpm { get; set; }

    // PostgreSQL has no unsigned integers, so the 32-bit counter and the fault code go into wider columns.
    public long PartCount { get; set; }
    public int FaultCode { get; set; }

    public static StoredReading From(MachineReading reading) => new()
    {
        MachineId = reading.MachineId,
        Timestamp = reading.Timestamp,
        State = reading.State,
        TemperatureC = reading.TemperatureC,
        SpeedRpm = reading.SpeedRpm,
        PartCount = reading.PartCount,
        FaultCode = reading.FaultCode,
    };
}

public sealed class Alarm
{
    public long Id { get; set; }
    public required string MachineId { get; set; }
    public AlarmKind Kind { get; set; }
    public required string Message { get; set; }
    public DateTimeOffset RaisedAt { get; set; }
    public DateTimeOffset? ClearedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }

    public AlarmInfo ToInfo() => new(Id, MachineId, Kind, Message, RaisedAt, ClearedAt, AcknowledgedAt);
}
