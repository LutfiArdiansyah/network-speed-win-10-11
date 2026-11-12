using System.Globalization;

namespace NetworkSpeed.Core.Display;

/// <summary>Format latency untuk overlay dan tooltip (spec X-01).</summary>
public static class LatencyFormatter
{
    public const string NoReading = "—";
    public const string Timeout = "timeout";

    public static string Format(LatencyReading? latency)
    {
        if (latency is null)
        {
            return NoReading;
        }

        if (latency.RoundTrip is not { } roundTrip)
        {
            return Timeout;
        }

        var ms = Math.Round(roundTrip.TotalMilliseconds, MidpointRounding.AwayFromZero);
        return ms < 1 ? "<1 ms" : ms.ToString("0", CultureInfo.InvariantCulture) + " ms";
    }
}
