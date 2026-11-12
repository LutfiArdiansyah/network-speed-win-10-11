using NetworkSpeed.Core.Formatting;
using NetworkSpeed.Core.Sampling;
using NetworkSpeed.Core.Settings;

namespace NetworkSpeed.Core.Display;

/// <summary>Hasil ping. <see cref="RoundTrip"/> null berarti timeout (spec X-01).</summary>
public sealed record LatencyReading(TimeSpan? RoundTrip);

/// <summary>Teks overlay beserta status alert per arah (spec D-03, D-04).</summary>
public sealed record OverlayText(IReadOnlyList<string> Lines, bool IsUpAlert, bool IsDownAlert);

/// <summary>Menyusun teks overlay dari hasil sampling dan pengaturan.</summary>
public static class OverlayTextBuilder
{
    public const string OfflineText = "Offline";
    private const string Separator = "  ";

    public static OverlayText Build(SpeedReading? reading, UserSettings settings, LatencyReading? latency)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (reading is { IsOffline: true })
        {
            return new OverlayText([OfflineText], false, false);
        }

        var up = reading?.UpBytesPerSecond ?? 0;
        var down = reading?.DownBytesPerSecond ?? 0;
        var upText = "↑ " + SpeedFormatter.Format(up, settings.UnitMode);
        var downText = "↓ " + SpeedFormatter.Format(down, settings.UnitMode);
        var threshold = settings.AlertThresholdBytesPerSecond;
        var isUpAlert = threshold is { } t1 && up > t1;
        var isDownAlert = threshold is { } t2 && down > t2;

        if (settings.Layout == OverlayLayout.TwoLines)
        {
            // Latency tidak ditampilkan di dua baris agar lebar overlay tetap stabil (tetap ada di tooltip).
            return new OverlayText([upText, downText], isUpAlert, isDownAlert);
        }

        var line = upText + Separator + downText;
        if (settings.ShowLatencyOnOverlay)
        {
            line += Separator + LatencyFormatter.Format(latency);
        }

        return new OverlayText([line], isUpAlert, isDownAlert);
    }
}
