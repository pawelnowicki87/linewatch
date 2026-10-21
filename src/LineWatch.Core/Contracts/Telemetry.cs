namespace LineWatch.Core.Contracts;

// Shapes shared by the API and the dashboard, over REST and SignalR.

public sealed record MachineStatus(
    string Id,
    string Name,
    bool Online,
    double MaxTemperatureC,
    MachineReading? Latest,
    IReadOnlyList<AlarmKind> ActiveAlarms);

public sealed record ReadingPoint(DateTimeOffset Timestamp, MachineState State, double TemperatureC, int SpeedRpm);

public sealed record AlarmInfo(
    long Id,
    string MachineId,
    AlarmKind Kind,
    string Message,
    DateTimeOffset RaisedAt,
    DateTimeOffset? ClearedAt,
    DateTimeOffset? AcknowledgedAt);

/// <summary>Messages the server pushes to dashboards connected to the telemetry hub.</summary>
public interface ITelemetryClient
{
    Task MachineUpdated(MachineStatus status);
    Task AlarmChanged(AlarmInfo alarm);
}
