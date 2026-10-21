using Microsoft.EntityFrameworkCore;

namespace LineWatch.Api.Data;

/// <summary>
/// One reading per machine per second adds up quickly, and the dashboard only looks back an hour.
/// </summary>
public sealed class ReadingRetention(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<ReadingRetention> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var keepFor = TimeSpan.FromHours(configuration.GetValue("ReadingRetentionHours", 24));
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10), time);

        try
        {
            do
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<LineWatchDbContext>();
                    var cutoff = time.GetUtcNow() - keepFor;

                    var deleted = await db.Readings
                        .Where(r => r.Timestamp < cutoff)
                        .ExecuteDeleteAsync(stoppingToken);

                    if (deleted > 0)
                        logger.LogInformation("Deleted {Count} readings older than {Cutoff:u}", deleted, cutoff);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Reading cleanup failed, will retry on the next run");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
