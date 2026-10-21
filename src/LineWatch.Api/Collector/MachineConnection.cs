using System.Net;
using System.Net.Sockets;
using FluentModbus;
using LineWatch.Core;

namespace LineWatch.Api.Collector;

/// <summary>
/// One Modbus TCP connection to one machine. It connects lazily and drops the socket
/// after any failure, so the next poll starts from a clean connection.
/// </summary>
public sealed class MachineConnection(MachineEndpoint endpoint, TimeSpan timeout) : IDisposable
{
    private readonly ModbusTcpClient _client = new()
    {
        ConnectTimeout = (int)timeout.TotalMilliseconds,
        ReadTimeout = (int)timeout.TotalMilliseconds,
    };

    public MachineEndpoint Endpoint => endpoint;

    public async Task<ushort[]> ReadRegistersAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        try
        {
            if (!_client.IsConnected)
                await ConnectAsync(timeoutCts.Token);

            var registers = await _client.ReadHoldingRegistersAsync<ushort>(
                endpoint.UnitId, RegisterMap.State, RegisterMap.Length, timeoutCts.Token);

            return registers.ToArray();
        }
        catch
        {
            _client.Disconnect();
            throw;
        }
    }

    private async Task ConnectAsync(CancellationToken cancellationToken)
    {
        // The client only takes IP addresses, and in Docker the machines are known by name.
        var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, AddressFamily.InterNetwork, cancellationToken);
        if (addresses.Length == 0)
            throw new SocketException((int)SocketError.HostNotFound);

        var remote = new IPEndPoint(addresses[0], endpoint.Port);
        await Task.Run(() => _client.Connect(remote, ModbusEndianness.BigEndian), cancellationToken);
    }

    public void Dispose() => _client.Dispose();
}
