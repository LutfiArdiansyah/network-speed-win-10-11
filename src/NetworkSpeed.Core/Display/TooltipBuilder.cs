using System.Globalization;

namespace NetworkSpeed.Core.Display;

/// <summary>Data tooltip overlay (spec F-06). Nilai null tidak ditampilkan.</summary>
public sealed record TooltipData(
    string? AdapterName,
    string? Ssid,
    string? Ipv4,
    ulong SessionSentBytes,
    ulong SessionReceivedBytes,
    LatencyReading? Latency,
    string? HiddenReason);

/// <summary>Menyusun teks tooltip. Tooltip hanya tampil di layar; isinya tidak boleh dicatat ke log (spec §7).</summary>
public static class TooltipBuilder
{
    private const double Scale = 1024;
    private static readonly string[] Units = ["KB", "MB", "GB", "TB"];

    public static string Build(TooltipData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var lines = new List<string>();
        AddIfPresent(lines, "Adapter", data.AdapterName);
        AddIfPresent(lines, "SSID", data.Ssid);
        AddIfPresent(lines, "IPv4", data.Ipv4);
        lines.Add($"Session: ↑ {FormatBytes(data.SessionSentBytes)}  ↓ {FormatBytes(data.SessionReceivedBytes)}");
        if (data.Latency is not null)
        {
            lines.Add("Latency: " + LatencyFormatter.Format(data.Latency));
        }

        AddIfPresent(lines, "Overlay hidden", data.HiddenReason);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Total byte dengan skala 1024: <c>512 B</c>, <c>5.0 MB</c>, <c>1.5 GB</c>.</summary>
    public static string FormatBytes(ulong bytes)
    {
        if (bytes < Scale)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        var value = bytes / Scale;
        var unit = 0;
        while (value >= Scale && unit < Units.Length - 1)
        {
            value /= Scale;
            unit++;
        }

        return value.ToString("0.0", CultureInfo.InvariantCulture) + " " + Units[unit];
    }

    private static void AddIfPresent(List<string> lines, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            lines.Add($"{label}: {value}");
        }
    }
}
