namespace LineWatch.Simulator;

public sealed class SimulatorOptions
{
    public const string Section = "Simulator";

    public int Port { get; set; } = 5020;
    public int TickMilliseconds { get; set; } = 500;

    // How often, on average, each machine changes over, faults or starts to overheat.
    public double ChangeoversPerHour { get; set; } = 10;
    public double FaultsPerHour { get; set; } = 6;
    public double OverheatsPerHour { get; set; } = 6;

    public List<MachineProfile> Machines { get; set; } = [];
}

public sealed class MachineProfile
{
    public string Id { get; set; } = "";
    public byte UnitId { get; set; }
    public double AmbientTemperatureC { get; set; } = 22;
    public double IdleTemperatureC { get; set; } = 30;
    public double RunningTemperatureC { get; set; } = 60;
    public double OverheatRiseC { get; set; } = 25;
    public int NominalSpeedRpm { get; set; } = 1500;
    public double CycleSeconds { get; set; } = 10;
}
