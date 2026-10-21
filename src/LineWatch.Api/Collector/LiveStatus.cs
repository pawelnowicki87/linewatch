using System.Collections.Concurrent;
using LineWatch.Core;
using LineWatch.Core.Contracts;
using Microsoft.Extensions.Options;

namespace LineWatch.Api.Collector;

/// <summary>
/// Latest known state of every machine, kept in memory so the dashboard's first load
/// does not have to query the readings table.
/// </summary>
public sealed class LiveStatus
{
    private readonly IReadOnlyList<string> _order;
    private readonly ConcurrentDictionary<string, MachineStatus> _machines;

    public LiveStatus(IOptions<CollectorOptions> options)
    {
        var machines = options.Value.Machines;
        _order = machines.Select(m => m.Id).ToList();
        _machines = new ConcurrentDictionary<string, MachineStatus>(machines.ToDictionary(
            m => m.Id,
            m => new MachineStatus(m.Id, m.Name, Online: false, m.MaxTemperatureC, Latest: null, ActiveAlarms: [])));
    }

    public IReadOnlyList<MachineStatus> All => _order.Select(id => _machines[id]).ToList();

    public MachineStatus? Find(string machineId) => _machines.GetValueOrDefault(machineId);

    public MachineStatus Update(string machineId, MachineReading? reading, IEnumerable<AlarmKind> activeAlarms) =>
        _machines.AddOrUpdate(
            machineId,
            _ => throw new KeyNotFoundException($"Unknown machine {machineId}"),
            (_, current) => current with
            {
                Online = reading is not null,
                // Keep the last good reading while the machine is offline, the dashboard greys it out.
                Latest = reading ?? current.Latest,
                ActiveAlarms = activeAlarms.Order().ToList(),
            });
}
