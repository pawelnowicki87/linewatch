using LineWatch.Core;

namespace LineWatch.Core.Tests;

public class RegisterMapTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Decode_reads_what_encode_wrote()
    {
        var registers = new ushort[RegisterMap.Length];
        RegisterMap.Encode(registers, MachineState.Running, 71.4, 1450, 123_456, 0);

        var reading = RegisterMap.Decode("mill-1", Now, registers);

        Assert.Equal(MachineState.Running, reading.State);
        Assert.Equal(71.4, reading.TemperatureC, precision: 1);
        Assert.Equal(1450, reading.SpeedRpm);
        Assert.Equal(123_456u, reading.PartCount);
        Assert.Equal(0, reading.FaultCode);
    }

    [Fact]
    public void Temperature_is_signed()
    {
        var registers = new ushort[RegisterMap.Length];
        RegisterMap.Encode(registers, MachineState.Idle, -12.5, 0, 0, 0);

        Assert.Equal(-12.5, RegisterMap.Decode("freezer", Now, registers).TemperatureC, precision: 1);
    }

    [Fact]
    public void Part_count_spans_two_registers_high_word_first()
    {
        var registers = new ushort[RegisterMap.Length];
        registers[RegisterMap.PartCountHigh] = 0x0001;
        registers[RegisterMap.PartCountLow] = 0x0002;

        Assert.Equal(65_538u, RegisterMap.Decode("press", Now, registers).PartCount);
    }

    [Fact]
    public void Unknown_state_is_rejected()
    {
        var registers = new ushort[RegisterMap.Length];
        registers[RegisterMap.State] = 7;

        Assert.Throws<InvalidDataException>(() => RegisterMap.Decode("mill-1", Now, registers));
    }

    [Fact]
    public void Too_few_registers_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => RegisterMap.Decode("mill-1", Now, new ushort[3]));
    }
}
