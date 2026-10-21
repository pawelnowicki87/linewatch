using LineWatch.Core;

namespace LineWatch.Simulator;

/// <summary>
/// A machine that is mostly running, sometimes stopped for a changeover, sometimes faulted,
/// and once in a while overheating. Values move gradually so the charts look like real sensors.
/// </summary>
public sealed class SimulatedMachine(MachineProfile profile, SimulatorOptions options, Random random)
{
    private static readonly ushort[] FaultCodes = [101, 205, 310, 412];

    private MachineState _state = MachineState.Running;
    private TimeSpan _stateLeft;
    private TimeSpan _overheatLeft;
    private double _temperature = profile.RunningTemperatureC;
    private double _speed = profile.NominalSpeedRpm;
    private double _cycleProgress;
    private uint _partCount;
    private ushort _faultCode;

    public MachineProfile Profile => profile;

    public void Advance(TimeSpan dt)
    {
        UpdateState(dt);

        var target = _state switch
        {
            MachineState.Running => profile.RunningTemperatureC,
            MachineState.Idle => profile.IdleTemperatureC,
            _ => profile.AmbientTemperatureC,
        };
        if (_overheatLeft > TimeSpan.Zero)
        {
            target += profile.OverheatRiseC;
            _overheatLeft -= dt;
        }

        // First-order lag towards the target plus a bit of sensor noise.
        _temperature += (target - _temperature) * 0.1 * dt.TotalSeconds + Noise(0.3);

        var targetSpeed = _state == MachineState.Running ? profile.NominalSpeedRpm : 0;
        _speed += (targetSpeed - _speed) * Math.Min(1, 1.5 * dt.TotalSeconds);

        if (_state == MachineState.Running)
        {
            _cycleProgress += dt.TotalSeconds / profile.CycleSeconds;
            if (_cycleProgress >= 1)
            {
                _cycleProgress -= 1;
                _partCount++;
            }
        }
    }

    public void WriteTo(Span<ushort> registers)
    {
        // A stopped spindle reads a flat zero, not noise around it.
        var speed = _speed < 5 ? 0 : (int)Math.Round(_speed + Noise(4));
        RegisterMap.Encode(registers, _state, _temperature, speed, _partCount, _faultCode);
    }

    private void UpdateState(TimeSpan dt)
    {
        if (_state != MachineState.Running)
        {
            _stateLeft -= dt;
            if (_stateLeft <= TimeSpan.Zero)
            {
                _state = MachineState.Running;
                _faultCode = 0;
            }
            return;
        }

        if (Happens(options.FaultsPerHour, dt))
        {
            _state = MachineState.Fault;
            _faultCode = FaultCodes[random.Next(FaultCodes.Length)];
            _stateLeft = TimeSpan.FromSeconds(random.Next(20, 41));
        }
        else if (Happens(options.ChangeoversPerHour, dt))
        {
            _state = MachineState.Idle;
            _stateLeft = TimeSpan.FromSeconds(random.Next(15, 31));
        }
        else if (_overheatLeft <= TimeSpan.Zero && Happens(options.OverheatsPerHour, dt))
        {
            _overheatLeft = TimeSpan.FromSeconds(random.Next(60, 91));
        }
    }

    private bool Happens(double perHour, TimeSpan dt) => random.NextDouble() < perHour * dt.TotalHours;

    private double Noise(double amplitude) => (random.NextDouble() * 2 - 1) * amplitude;
}
