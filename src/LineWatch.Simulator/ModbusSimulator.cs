using System.Net;
using FluentModbus;
using LineWatch.Core;
using Microsoft.Extensions.Options;

namespace LineWatch.Simulator;

/// <summary>
/// Serves every simulated machine from one Modbus TCP server, one unit id per machine,
/// the way a gateway in front of several serial devices would.
/// </summary>
public sealed class ModbusSimulator(
    IOptions<SimulatorOptions> options,
    ILogger<ModbusSimulator> logger,
    ILoggerFactory loggerFactory) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (settings.Machines.Count == 0)
            throw new InvalidOperationException("No machines configured under Simulator:Machines.");

        using var server = new ModbusTcpServer(loggerFactory.CreateLogger<ModbusTcpServer>(), isAsynchronous: true);
        var random = new Random();
        var machines = settings.Machines
            .Select(profile => new SimulatedMachine(profile, settings, random))
            .ToList();

        foreach (var machine in machines)
            server.AddUnit(machine.Profile.UnitId);

        server.Start(new IPEndPoint(IPAddress.Any, settings.Port));
        logger.LogInformation("Simulating {Count} machines on Modbus TCP port {Port}", machines.Count, settings.Port);

        var tick = TimeSpan.FromMilliseconds(settings.TickMilliseconds);
        using var timer = new PeriodicTimer(tick);
        var registers = new ushort[RegisterMap.Length];

        do
        {
            foreach (var machine in machines)
            {
                machine.Advance(tick);
                machine.WriteTo(registers);

                lock (server.Lock)
                {
                    var holding = server.GetHoldingRegisters(machine.Profile.UnitId);
                    for (var address = 0; address < registers.Length; address++)
                        holding.SetBigEndian(address, registers[address]);
                }
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
