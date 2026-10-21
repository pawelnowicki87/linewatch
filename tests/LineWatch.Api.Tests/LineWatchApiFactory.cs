using System.Net;
using System.Net.Sockets;
using FluentModbus;
using LineWatch.Api.Collector;
using LineWatch.Core;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace LineWatch.Api.Tests;

/// <summary>
/// The real API against a real PostgreSQL in Docker and a Modbus server running inside the test,
/// so the whole path from register to REST response is exercised.
/// </summary>
public sealed class LineWatchApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string MachineId = "test-mill";
    public const string UnreachableMachineId = "unplugged";
    public const double MaxTemperatureC = 80;

    private const byte UnitId = 1;

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17").Build();
    private readonly ModbusTcpServer _modbus = new(isAsynchronous: true);
    private readonly int _modbusPort = FreePort();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _modbus.AddUnit(UnitId);
        _modbus.Start(new IPEndPoint(IPAddress.Loopback, _modbusPort));
        SetMachine(MachineState.Running, temperatureC: 60);
    }

    public void SetMachine(MachineState state, double temperatureC, ushort faultCode = 0)
    {
        var registers = new ushort[RegisterMap.Length];
        RegisterMap.Encode(registers, state, temperatureC, speedRpm: 1450, partCount: 70_000, faultCode);

        lock (_modbus.Lock)
        {
            var holding = _modbus.GetHoldingRegisters(UnitId);
            for (var address = 0; address < registers.Length; address++)
                holding.SetBigEndian(address, registers[address]);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:LineWatch", _postgres.GetConnectionString());

        builder.ConfigureTestServices(services => services.PostConfigure<CollectorOptions>(options =>
        {
            options.PollIntervalMilliseconds = 100;
            options.TimeoutMilliseconds = 300;
            options.FailedPollsBeforeOffline = 2;
            options.Machines =
            [
                new MachineEndpoint
                {
                    Id = MachineId, Name = "Test mill", Host = "127.0.0.1", Port = _modbusPort,
                    UnitId = UnitId, MaxTemperatureC = MaxTemperatureC, HysteresisC = 3,
                },
                new MachineEndpoint
                {
                    Id = UnreachableMachineId, Name = "Nothing listens here", Host = "127.0.0.1", Port = FreePort(),
                    UnitId = 1, MaxTemperatureC = 100,
                },
            ];
        }));
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        _modbus.Dispose();
        await _postgres.DisposeAsync();
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
