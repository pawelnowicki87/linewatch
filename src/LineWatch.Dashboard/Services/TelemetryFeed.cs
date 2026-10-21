using LineWatch.Core.Contracts;
using Microsoft.AspNetCore.SignalR.Client;

namespace LineWatch.Dashboard.Services;

/// <summary>
/// Client-side picture of the line: loads a snapshot over REST, then keeps it current
/// from the SignalR hub. Pages read from here and re-render on <see cref="Changed"/>.
/// </summary>
public sealed class TelemetryFeed(LineWatchApi api, ILogger<TelemetryFeed> logger) : IAsyncDisposable
{
    public static readonly TimeSpan HistoryWindow = TimeSpan.FromMinutes(15);
    private const int AlarmsKept = 200;

    private readonly List<string> _order = [];
    private readonly Dictionary<string, MachineStatus> _machines = [];
    private readonly Dictionary<string, List<ReadingPoint>> _history = [];
    private readonly List<AlarmInfo> _alarms = [];
    private HubConnection? _hub;
    private Task? _started;

    public event Action? Changed;

    public bool Loaded { get; private set; }

    public HubConnectionState Connection => _hub?.State ?? HubConnectionState.Disconnected;

    public IReadOnlyList<MachineStatus> Machines => _order.Select(id => _machines[id]).ToList();

    public IReadOnlyList<AlarmInfo> Alarms => _alarms;

    public int OpenUnacknowledged => _alarms.Count(a => a.ClearedAt is null && a.AcknowledgedAt is null);

    public MachineStatus? Machine(string id) => _machines.GetValueOrDefault(id);

    public IReadOnlyList<ReadingPoint> History(string id) => _history.TryGetValue(id, out var points) ? points : [];

    public Task StartAsync() => _started ??= ConnectAsync();

    public void Apply(AlarmInfo alarm)
    {
        var index = _alarms.FindIndex(a => a.Id == alarm.Id);
        if (index >= 0)
            _alarms[index] = alarm;
        else
        {
            _alarms.Insert(0, alarm);
            if (_alarms.Count > AlarmsKept)
                _alarms.RemoveAt(_alarms.Count - 1);
        }

        Changed?.Invoke();
    }

    private async Task ConnectAsync()
    {
        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(api.BaseAddress, "hubs/telemetry"))
            .WithAutomaticReconnect()
            .Build();

        _hub.On<MachineStatus>(nameof(ITelemetryClient.MachineUpdated), OnMachineUpdated);
        _hub.On<AlarmInfo>(nameof(ITelemetryClient.AlarmChanged), Apply);
        _hub.Reconnecting += _ => Notify();
        _hub.Closed += _ => Notify();
        // Whatever happened while we were away is only in the database, so reload it.
        _hub.Reconnected += _ => LoadSnapshotAsync();

        // Automatic reconnect only covers connections that were up once, so the first start retries by hand.
        while (true)
        {
            try
            {
                await _hub.StartAsync();
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Telemetry hub unavailable, retrying");
                await Notify();
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }

        // Subscribed before loading, so no update can slip in between the snapshot and the stream.
        await LoadSnapshotAsync();
    }

    private async Task LoadSnapshotAsync()
    {
        try
        {
            var machines = await api.GetMachinesAsync();
            var histories = await Task.WhenAll(machines.Select(m => api.GetReadingsAsync(m.Id, (int)HistoryWindow.TotalMinutes)));
            var alarms = await api.GetAlarmsAsync(AlarmsKept);

            _order.Clear();
            _order.AddRange(machines.Select(m => m.Id));
            for (var i = 0; i < machines.Count; i++)
            {
                _machines[machines[i].Id] = machines[i];
                _history[machines[i].Id] = histories[i];
            }

            _alarms.Clear();
            _alarms.AddRange(alarms);
            Loaded = true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Loading the current state failed");
        }

        await Notify();
    }

    private void OnMachineUpdated(MachineStatus status)
    {
        if (!_machines.ContainsKey(status.Id))
            _order.Add(status.Id);
        _machines[status.Id] = status;

        if (status is { Online: true, Latest: { } reading })
        {
            var points = _history.TryGetValue(status.Id, out var existing) ? existing : _history[status.Id] = [];
            if (points.Count == 0 || points[^1].Timestamp < reading.Timestamp)
                points.Add(new ReadingPoint(reading.Timestamp, reading.State, reading.TemperatureC, reading.SpeedRpm));

            var cutoff = reading.Timestamp - HistoryWindow;
            var stale = points.FindIndex(p => p.Timestamp >= cutoff);
            if (stale > 0)
                points.RemoveRange(0, stale);
        }

        Changed?.Invoke();
    }

    private Task Notify()
    {
        Changed?.Invoke();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_hub is not null)
            await _hub.DisposeAsync();
    }
}
