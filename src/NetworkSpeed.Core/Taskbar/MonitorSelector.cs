namespace NetworkSpeed.Core.Taskbar;

/// <summary>Memilih taskbar yang menampilkan overlay (spec E-03).</summary>
public static class MonitorSelector
{
    /// <summary>
    /// Monitor pilihan yang tidak ada lagi (mis. dicabut) jatuh ke taskbar utama, supaya overlay tidak hilang.
    /// </summary>
    public static IReadOnlyList<TaskbarLayout> Select(IReadOnlyList<TaskbarLayout> layouts, MonitorMode mode, string? specificMonitorId)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        if (layouts.Count == 0)
        {
            return [];
        }

        if (mode == MonitorMode.All)
        {
            return layouts;
        }

        if (mode == MonitorMode.Specific && specificMonitorId is not null)
        {
            var match = layouts.FirstOrDefault(l => string.Equals(l.MonitorId, specificMonitorId, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return [match];
            }
        }

        return [layouts.FirstOrDefault(l => l.IsPrimary) ?? layouts[0]];
    }
}
