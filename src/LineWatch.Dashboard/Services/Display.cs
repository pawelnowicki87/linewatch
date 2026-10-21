using System.Globalization;
using LineWatch.Core;

namespace LineWatch.Dashboard.Services;

public static class Display
{
    public static string Describe(AlarmKind kind) => kind switch
    {
        AlarmKind.HighTemperature => "High temperature",
        AlarmKind.MachineFault => "Machine fault",
        AlarmKind.CommunicationLost => "Communication lost",
        _ => kind.ToString(),
    };

    public static string Temperature(double celsius) => celsius.ToString("0.0", CultureInfo.InvariantCulture) + " °C";

    public static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Time(DateTimeOffset at) => at.ToLocalTime().ToString("HH:mm:ss");

    public static string Duration(TimeSpan span) => span.TotalHours >= 1
        ? $"{(int)span.TotalHours} h {span.Minutes} min"
        : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} min {span.Seconds} s" : $"{span.Seconds} s";
}
