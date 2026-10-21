namespace LineWatch.Api.Collector;

public sealed class CollectorOptions
{
    public const string Section = "Collector";

    public int PollIntervalMilliseconds { get; set; } = 1000;
    public int TimeoutMilliseconds { get; set; } = 800;
    public int FailedPollsBeforeOffline { get; set; } = 3;
    public List<MachineEndpoint> Machines { get; set; } = [];
}

public sealed class MachineEndpoint
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 502;
    public byte UnitId { get; set; } = 1;
    public double MaxTemperatureC { get; set; }
    public double HysteresisC { get; set; } = 3;
}
