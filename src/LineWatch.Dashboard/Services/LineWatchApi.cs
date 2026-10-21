using System.Net;
using System.Net.Http.Json;
using LineWatch.Core.Contracts;

namespace LineWatch.Dashboard.Services;

public sealed class LineWatchApi(HttpClient http)
{
    public Uri BaseAddress => http.BaseAddress!;

    public async Task<List<MachineStatus>> GetMachinesAsync(CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<List<MachineStatus>>("api/machines", cancellationToken) ?? [];

    public async Task<List<ReadingPoint>> GetReadingsAsync(string machineId, int minutes, CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<List<ReadingPoint>>(
            $"api/machines/{Uri.EscapeDataString(machineId)}/readings?minutes={minutes}", cancellationToken) ?? [];

    public async Task<List<AlarmInfo>> GetAlarmsAsync(int limit, CancellationToken cancellationToken = default) =>
        await http.GetFromJsonAsync<List<AlarmInfo>>($"api/alarms?limit={limit}", cancellationToken) ?? [];

    public async Task<AlarmInfo?> AcknowledgeAsync(long alarmId, CancellationToken cancellationToken = default)
    {
        using var response = await http.PostAsync($"api/alarms/{alarmId}/acknowledge", content: null, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AlarmInfo>(cancellationToken);
    }
}
