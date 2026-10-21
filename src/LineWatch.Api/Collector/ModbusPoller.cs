using LineWatch.Api.Data;
using LineWatch.Api.Realtime;
using LineWatch.Core;
using LineWatch.Core.Contracts;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LineWatch.Api.Collector;

/// <summary>
/// Polls every machine once per interval, stores the readings, opens and closes alarms
/// and pushes the result to connected dashboards.
/// </summary>
public sealed class ModbusPoller(
    IOptions<CollectorOptions> options,
    IServiceScopeFactory scopeFactory,
    LiveStatus liveStatus,
    IHubContext<TelemetryHub, ITelemetryClient> hub,
    TimeProvider time,
    ILogger<ModbusPoller> logger) : BackgroundService
{
    private sealed record PolledMachine(MachineConnection Connection, AlarmTracker Tracker);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        var timeout = TimeSpan.FromMilliseconds(settings.TimeoutMilliseconds);
        var machines = settings.Machines
            .Select(m => new PolledMachine(
                new MachineConnection(m, timeout),
                new AlarmTracker(m.Id, new AlarmLimits(m.MaxTemperatureC, m.HysteresisC, settings.FailedPollsBeforeOffline))))
            .ToList();

        try
        {
            await RestoreOpenAlarmsAsync(machines, stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(settings.PollIntervalMilliseconds), time);
            do
            {
                try
                {
                    await PollOnceAsync(machines, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A database hiccup should cost one sample, not stop the collector.
                    logger.LogError(ex, "Poll cycle failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            foreach (var machine in machines)
                machine.Connection.Dispose();
        }
    }

    private async Task RestoreOpenAlarmsAsync(List<PolledMachine> machines, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LineWatchDbContext>();

        var open = await db.Alarms
            .Where(a => a.ClearedAt == null)
            .Select(a => new { a.MachineId, a.Kind })
            .ToListAsync(cancellationToken);

        foreach (var machine in machines)
            machine.Tracker.Restore(open.Where(a => a.MachineId == machine.Connection.Endpoint.Id).Select(a => a.Kind));
    }

    private async Task PollOnceAsync(List<PolledMachine> machines, CancellationToken cancellationToken)
    {
        var readings = await Task.WhenAll(machines.Select(m => ReadAsync(m, cancellationToken)));
        var now = time.GetUtcNow();

        var changes = new List<AlarmChange>();
        for (var i = 0; i < machines.Count; i++)
        {
            var tracker = machines[i].Tracker;
            changes.AddRange(readings[i] is { } reading ? tracker.OnReading(reading) : tracker.OnPollFailed(now));
        }

        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LineWatchDbContext>();

        db.Readings.AddRange(readings.OfType<MachineReading>().Select(StoredReading.From));
        var touchedAlarms = await ApplyAlarmChangesAsync(db, changes, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < machines.Count; i++)
        {
            var status = liveStatus.Update(machines[i].Connection.Endpoint.Id, readings[i], machines[i].Tracker.Active);
            await hub.Clients.All.MachineUpdated(status);
        }

        foreach (var alarm in touchedAlarms)
            await hub.Clients.All.AlarmChanged(alarm.ToInfo());
    }

    private async Task<MachineReading?> ReadAsync(PolledMachine machine, CancellationToken cancellationToken)
    {
        var endpoint = machine.Connection.Endpoint;
        try
        {
            var registers = await machine.Connection.ReadRegistersAsync(cancellationToken);
            return RegisterMap.Decode(endpoint.Id, time.GetUtcNow(), registers);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogDebug(ex, "Reading {Machine} at {Host}:{Port} unit {Unit} failed",
                endpoint.Id, endpoint.Host, endpoint.Port, endpoint.UnitId);
            return null;
        }
    }

    private static async Task<List<Alarm>> ApplyAlarmChangesAsync(
        LineWatchDbContext db, List<AlarmChange> changes, CancellationToken cancellationToken)
    {
        var touched = new List<Alarm>();

        foreach (var change in changes)
        {
            if (change.Raised)
            {
                var alarm = new Alarm
                {
                    MachineId = change.MachineId,
                    Kind = change.Kind,
                    Message = change.Message,
                    RaisedAt = change.At,
                };
                db.Alarms.Add(alarm);
                touched.Add(alarm);
                continue;
            }

            var open = await db.Alarms.FirstOrDefaultAsync(
                a => a.MachineId == change.MachineId && a.Kind == change.Kind && a.ClearedAt == null,
                cancellationToken);

            if (open is not null)
            {
                open.ClearedAt = change.At;
                touched.Add(open);
            }
        }

        return touched;
    }
}
