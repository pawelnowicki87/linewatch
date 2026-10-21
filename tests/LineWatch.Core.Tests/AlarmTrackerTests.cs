using LineWatch.Core;

namespace LineWatch.Core.Tests;

public class AlarmTrackerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private readonly AlarmTracker _tracker = new("oven", new AlarmLimits(MaxTemperatureC: 200, HysteresisC: 5));

    private static MachineReading Reading(double temperature, MachineState state = MachineState.Running, ushort faultCode = 0) =>
        new("oven", T0, state, temperature, SpeedRpm: 0, PartCount: 0, faultCode);

    [Fact]
    public void Temperature_over_the_limit_raises_one_alarm()
    {
        var first = _tracker.OnReading(Reading(201));
        var second = _tracker.OnReading(Reading(204));

        var change = Assert.Single(first);
        Assert.True(change.Raised);
        Assert.Equal(AlarmKind.HighTemperature, change.Kind);
        Assert.Empty(second);
    }

    [Fact]
    public void Alarm_stays_open_inside_the_hysteresis_band()
    {
        _tracker.OnReading(Reading(201));

        Assert.Empty(_tracker.OnReading(Reading(198)));
        Assert.Contains(AlarmKind.HighTemperature, _tracker.Active);
    }

    [Fact]
    public void Alarm_clears_below_the_hysteresis_band()
    {
        _tracker.OnReading(Reading(201));

        var change = Assert.Single(_tracker.OnReading(Reading(194.9)));

        Assert.False(change.Raised);
        Assert.Empty(_tracker.Active);
    }

    [Fact]
    public void Fault_state_raises_and_clears_a_fault_alarm()
    {
        var raised = Assert.Single(_tracker.OnReading(Reading(150, MachineState.Fault, faultCode: 17)));
        var cleared = Assert.Single(_tracker.OnReading(Reading(150, MachineState.Idle)));

        Assert.Equal(AlarmKind.MachineFault, raised.Kind);
        Assert.Contains("17", raised.Message);
        Assert.False(cleared.Raised);
    }

    [Fact]
    public void Communication_is_lost_only_after_the_configured_number_of_failed_polls()
    {
        Assert.Empty(_tracker.OnPollFailed(T0));
        Assert.Empty(_tracker.OnPollFailed(T0));

        var change = Assert.Single(_tracker.OnPollFailed(T0));
        Assert.Equal(AlarmKind.CommunicationLost, change.Kind);
        Assert.Empty(_tracker.OnPollFailed(T0));
    }

    [Fact]
    public void A_successful_poll_restores_communication_and_resets_the_counter()
    {
        for (var i = 0; i < 3; i++) _tracker.OnPollFailed(T0);

        var change = Assert.Single(_tracker.OnReading(Reading(150)));
        Assert.Equal(AlarmKind.CommunicationLost, change.Kind);
        Assert.False(change.Raised);

        Assert.Empty(_tracker.OnPollFailed(T0));
    }

    [Fact]
    public void Restored_alarms_are_not_raised_again()
    {
        _tracker.Restore([AlarmKind.HighTemperature]);

        Assert.Empty(_tracker.OnReading(Reading(210)));
    }
}
