namespace LineWatch.Core;

public enum MachineState
{
    Idle = 0,
    Running = 1,
    Fault = 2,
}

public sealed record MachineReading(
    string MachineId,
    DateTimeOffset Timestamp,
    MachineState State,
    double TemperatureC,
    int SpeedRpm,
    uint PartCount,
    ushort FaultCode);
