using LineWatch.Api.Collector;
using LineWatch.Api.Data;
using LineWatch.Core.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace LineWatch.Api.Endpoints;

public static class MachineEndpoints
{
    private const int MaxHistoryMinutes = 60;

    public static RouteGroupBuilder MapMachineEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/machines").WithTags("Machines");

        group.MapGet("/", (LiveStatus live) => live.All);

        group.MapGet("/{id}", Results<Ok<MachineStatus>, NotFound> (string id, LiveStatus live) =>
            live.Find(id) is { } status ? TypedResults.Ok(status) : TypedResults.NotFound());

        group.MapGet("/{id}/readings", GetReadingsAsync);

        return group;
    }

    private static async Task<Results<Ok<List<ReadingPoint>>, NotFound>> GetReadingsAsync(
        string id,
        int? minutes,
        LiveStatus live,
        LineWatchDbContext db,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (live.Find(id) is null)
            return TypedResults.NotFound();

        var window = TimeSpan.FromMinutes(Math.Clamp(minutes ?? 15, 1, MaxHistoryMinutes));
        var since = time.GetUtcNow() - window;

        var points = await db.Readings
            .AsNoTracking()
            .Where(r => r.MachineId == id && r.Timestamp >= since)
            .OrderBy(r => r.Timestamp)
            .Select(r => new ReadingPoint(r.Timestamp, r.State, r.TemperatureC, r.SpeedRpm))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(points);
    }
}
