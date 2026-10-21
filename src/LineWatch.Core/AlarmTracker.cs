namespace LineWatch.Core;

public enum AlarmKind
{
    HighTemperature,
    MachineFault,
    CommunicationLost,
}

public sealed record AlarmLimits(double MaxTemperatureC, double HysteresisC, int FailedPollsBeforeOffline = 3);

public sealed record AlarmChange(string MachineId, AlarmKind Kind, bool Raised, string Message, DateTimeOffset At);

/// <summary>
/// Decides when alarms for one machine start and end. It only reports changes,
/// so a temperature that stays too high produces one alarm, not one per poll.
/// Not thread-safe: the collector owns one tracker per machine and polls it sequentially.
/// </summary>
public sealed class AlarmTracker(string machineId, AlarmLimits limits)
{
    private readonly HashSet<AlarmKind> _active = [];
    private int _failedPolls;

    public IReadOnlyCollection<AlarmKind> Active => _active;

    /// <summary>Alarms that were already open before a restart, so they are not raised twice.</summary>
    public void Restore(IEnumerable<AlarmKind> openAlarms) => _active.UnionWith(openAlarms);

    public IReadOnlyList<AlarmChange> OnReading(MachineReading reading)
    {
        var changes = new List<AlarmChange>();
        _failedPolls = 0;

        if (_active.Contains(AlarmKind.CommunicationLost))
            Clear(AlarmKind.CommunicationLost, "Communication restored", reading.Timestamp, changes);

        // The deadband keeps a temperature hovering around the limit from
        // raising and clearing the alarm on every poll.
        if (reading.TemperatureC > limits.MaxTemperatureC)
            Raise(AlarmKind.HighTemperature,
                $"Temperature {reading.TemperatureC:0.0} °C above limit {limits.MaxTemperatureC:0.0} °C",
                reading.Timestamp, changes);
        else if (reading.TemperatureC < limits.MaxTemperatureC - limits.HysteresisC)
            Clear(AlarmKind.HighTemperature,
                $"Temperature back to {reading.TemperatureC:0.0} °C",
                reading.Timestamp, changes);

        if (reading.State == MachineState.Fault)
            Raise(AlarmKind.MachineFault, $"Machine reported fault code {reading.FaultCode}", reading.Timestamp, changes);
        else
            Clear(AlarmKind.MachineFault, "Fault cleared", reading.Timestamp, changes);

        return changes;
    }

    public IReadOnlyList<AlarmChange> OnPollFailed(DateTimeOffset at)
    {
        var changes = new List<AlarmChange>();
        _failedPolls++;

        if (_failedPolls >= limits.FailedPollsBeforeOffline)
            Raise(AlarmKind.CommunicationLost, $"No response for {_failedPolls} polls", at, changes);

        return changes;
    }

    private void Raise(AlarmKind kind, string message, DateTimeOffset at, List<AlarmChange> changes)
    {
        if (_active.Add(kind))
            changes.Add(new AlarmChange(machineId, kind, Raised: true, message, at));
    }

    private void Clear(AlarmKind kind, string message, DateTimeOffset at, List<AlarmChange> changes)
    {
        if (_active.Remove(kind))
            changes.Add(new AlarmChange(machineId, kind, Raised: false, message, at));
    }
}
