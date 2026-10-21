namespace LineWatch.Core;

/// <summary>
/// Holding register layout every machine exposes, starting at address 0.
/// The simulator writes it and the collector reads it, so both sides share this class.
/// </summary>
public static class RegisterMap
{
    public const ushort State = 0;
    public const ushort Temperature = 1;   // signed, tenths of a degree Celsius
    public const ushort Speed = 2;         // rpm
    public const ushort PartCountHigh = 3; // 32-bit counter, high word first
    public const ushort PartCountLow = 4;
    public const ushort FaultCode = 5;     // 0 when there is no fault

    public const ushort Length = 6;

    public static MachineReading Decode(string machineId, DateTimeOffset timestamp, ReadOnlySpan<ushort> registers)
    {
        if (registers.Length < Length)
            throw new ArgumentException($"Expected {Length} registers, got {registers.Length}.", nameof(registers));

        var rawState = registers[State];
        if (!Enum.IsDefined(typeof(MachineState), (int)rawState))
            throw new InvalidDataException($"Machine {machineId} reported unknown state {rawState}.");

        return new MachineReading(
            machineId,
            timestamp,
            (MachineState)rawState,
            TemperatureC: (short)registers[Temperature] / 10.0,
            SpeedRpm: registers[Speed],
            PartCount: (uint)registers[PartCountHigh] << 16 | registers[PartCountLow],
            FaultCode: registers[FaultCode]);
    }

    public static void Encode(
        Span<ushort> registers,
        MachineState state,
        double temperatureC,
        int speedRpm,
        uint partCount,
        ushort faultCode)
    {
        if (registers.Length < Length)
            throw new ArgumentException($"Expected room for {Length} registers, got {registers.Length}.", nameof(registers));

        registers[State] = (ushort)state;
        registers[Temperature] = (ushort)(short)Math.Round(temperatureC * 10);
        registers[Speed] = (ushort)Math.Clamp(speedRpm, 0, ushort.MaxValue);
        registers[PartCountHigh] = (ushort)(partCount >> 16);
        registers[PartCountLow] = (ushort)(partCount & 0xFFFF);
        registers[FaultCode] = faultCode;
    }
}
