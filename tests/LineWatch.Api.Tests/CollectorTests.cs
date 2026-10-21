using System.Net.Http.Json;
using LineWatch.Core;
using LineWatch.Core.Contracts;
using Microsoft.AspNetCore.SignalR.Client;

namespace LineWatch.Api.Tests;

public class CollectorTests(LineWatchApiFactory factory) : IClassFixture<LineWatchApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Readings_from_the_machine_reach_the_status_and_the_history()
    {
        factory.SetMachine(MachineState.Running, temperatureC: 64.5);

        var status = await Eventually(
            () => _client.GetFromJsonAsync<MachineStatus>($"api/machines/{LineWatchApiFactory.MachineId}"),
            s => s is { Online: true, Latest.TemperatureC: 64.5 });

        Assert.Equal(1450, status!.Latest!.SpeedRpm);
        Assert.Equal(70_000u, status.Latest.PartCount);

        var history = await _client.GetFromJsonAsync<List<ReadingPoint>>(
            $"api/machines/{LineWatchApiFactory.MachineId}/readings?minutes=1");
        Assert.NotEmpty(history!);
    }

    [Fact]
    public async Task High_temperature_opens_an_alarm_that_clears_once_the_machine_cools_down()
    {
        factory.SetMachine(MachineState.Running, temperatureC: 85);
        var open = await Eventually(OpenAlarms, alarms => alarms.Any(a => a.Kind == AlarmKind.HighTemperature));
        var alarm = open.Single(a => a.Kind == AlarmKind.HighTemperature);

        // Inside the 3 °C hysteresis band the alarm has to stay open.
        factory.SetMachine(MachineState.Running, temperatureC: 78);
        await Task.Delay(500);
        Assert.Contains(await OpenAlarms(), a => a.Id == alarm.Id);

        factory.SetMachine(MachineState.Running, temperatureC: 70);
        await Eventually(OpenAlarms, alarms => alarms.All(a => a.Id != alarm.Id));
    }

    [Fact]
    public async Task Acknowledging_keeps_the_first_timestamp()
    {
        factory.SetMachine(MachineState.Fault, temperatureC: 60, faultCode: 205);
        var open = await Eventually(OpenAlarms, alarms => alarms.Any(a => a.Kind == AlarmKind.MachineFault));
        var alarm = open.Single(a => a.Kind == AlarmKind.MachineFault);

        var first = await Acknowledge(alarm.Id);
        var second = await Acknowledge(alarm.Id);

        Assert.NotNull(first.AcknowledgedAt);
        Assert.Equal(first.AcknowledgedAt, second.AcknowledgedAt);
        Assert.Contains("205", first.Message);

        factory.SetMachine(MachineState.Running, temperatureC: 60);
    }

    [Fact]
    public async Task A_machine_that_does_not_answer_raises_communication_lost()
    {
        var alarms = await Eventually(
            () => _client.GetFromJsonAsync<List<AlarmInfo>>($"api/alarms?open=true&machineId={LineWatchApiFactory.UnreachableMachineId}"),
            list => list!.Any(a => a.Kind == AlarmKind.CommunicationLost));

        Assert.Single(alarms!);
        var status = await _client.GetFromJsonAsync<MachineStatus>($"api/machines/{LineWatchApiFactory.UnreachableMachineId}");
        Assert.False(status!.Online);
    }

    [Fact]
    public async Task Dashboards_get_machine_updates_pushed_over_signalr()
    {
        var received = new TaskCompletionSource<MachineStatus>(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var hub = new HubConnectionBuilder()
            .WithUrl(new Uri(factory.Server.BaseAddress, "hubs/telemetry"), options =>
            {
                options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
            })
            .Build();

        hub.On<MachineStatus>(nameof(ITelemetryClient.MachineUpdated), status =>
        {
            if (status.Id == LineWatchApiFactory.MachineId)
                received.TrySetResult(status);
        });
        await hub.StartAsync();

        var pushed = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(pushed.Online);
    }

    [Fact]
    public async Task Unknown_machine_returns_404()
    {
        var response = await _client.GetAsync("api/machines/no-such-machine/readings");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<List<AlarmInfo>> OpenAlarms() =>
        await _client.GetFromJsonAsync<List<AlarmInfo>>($"api/alarms?open=true&machineId={LineWatchApiFactory.MachineId}") ?? [];

    private async Task<AlarmInfo> Acknowledge(long id)
    {
        var response = await _client.PostAsync($"api/alarms/{id}/acknowledge", content: null);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AlarmInfo>())!;
    }

    // The collector works on its own schedule, so assertions wait for it instead of sleeping a fixed time.
    private static async Task<T> Eventually<T>(Func<Task<T>> probe, Func<T, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var value = await probe();
            if (condition(value))
                return value;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Condition not met in time, last value: {value}");
            await Task.Delay(100);
        }
    }
}
