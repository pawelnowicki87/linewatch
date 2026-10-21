namespace LineWatch.Api.Data;

public static class DatabaseClock
{
    /// <summary>
    /// PostgreSQL keeps timestamps to the microsecond. Trimming the extra ticks up front means the value
    /// we return or broadcast right away is the same one a later query will read back.
    /// </summary>
    public static DateTimeOffset GetUtcNowForDatabase(this TimeProvider time)
    {
        var now = time.GetUtcNow();
        return now.AddTicks(-(now.Ticks % TimeSpan.TicksPerMicrosecond));
    }
}
