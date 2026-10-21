using LineWatch.Api.Data;
using LineWatch.Api.Realtime;
using LineWatch.Core.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace LineWatch.Api.Endpoints;

public static class AlarmEndpoints
{
    public static RouteGroupBuilder MapAlarmEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/alarms").WithTags("Alarms");

        group.MapGet("/", GetAlarmsAsync);
        group.MapPost("/{id:long}/acknowledge", AcknowledgeAsync);

        return group;
    }

    private static async Task<List<AlarmInfo>> GetAlarmsAsync(
        bool? open,
        string? machineId,
        int? limit,
        LineWatchDbContext db,
        CancellationToken cancellationToken)
    {
        var query = db.Alarms.AsNoTracking();

        if (open is true)
            query = query.Where(a => a.ClearedAt == null);
        if (!string.IsNullOrEmpty(machineId))
            query = query.Where(a => a.MachineId == machineId);

        return await query
            .OrderByDescending(a => a.RaisedAt)
            .Take(Math.Clamp(limit ?? 50, 1, 500))
            .Select(a => new AlarmInfo(a.Id, a.MachineId, a.Kind, a.Message, a.RaisedAt, a.ClearedAt, a.AcknowledgedAt))
            .ToListAsync(cancellationToken);
    }

    private static async Task<Results<Ok<AlarmInfo>, NotFound>> AcknowledgeAsync(
        long id,
        LineWatchDbContext db,
        IHubContext<TelemetryHub, ITelemetryClient> hub,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var alarm = await db.Alarms.FindAsync([id], cancellationToken);
        if (alarm is null)
            return TypedResults.NotFound();

        // Acknowledging twice keeps the first operator's timestamp.
        if (alarm.AcknowledgedAt is null)
        {
            alarm.AcknowledgedAt = time.GetUtcNowForDatabase();
            await db.SaveChangesAsync(cancellationToken);
            await hub.Clients.All.AlarmChanged(alarm.ToInfo());
        }

        return TypedResults.Ok(alarm.ToInfo());
    }
}
