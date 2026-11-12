using NetworkSpeed.Core.Taskbar;

namespace NetworkSpeed.Core.Settings;

/// <summary>Satu pilihan posisi di Settings beserta alasan bila nonaktif (spec F-07a).</summary>
public sealed record PositionOption(OverlayPosition Position, bool IsEnabled, string? DisabledReason);

/// <summary>Logika murni jendela Settings.</summary>
public static class SettingsEditor
{
    /// <summary>
    /// Posisi aktif bila didukung di <b>semua</b> taskbar yang ditampilkan; alasan diambil dari taskbar pertama
    /// yang menolak. Tanpa data taskbar, semua posisi aktif (penempatan tetap aman lewat fallback F-07b).
    /// </summary>
    public static IReadOnlyList<PositionOption> PositionOptions(IReadOnlyList<TaskbarLayout> layouts)
    {
        ArgumentNullException.ThrowIfNull(layouts);
        return Enum.GetValues<OverlayPosition>()
            .Select(position =>
            {
                var rejection = layouts
                    .Select(l => PositionSupportEvaluator.Evaluate(l).Single(s => s.Position == position))
                    .FirstOrDefault(s => !s.IsSupported);
                return new PositionOption(position, rejection is null, rejection?.Reason);
            })
            .ToList();
    }

    /// <summary>Latency di overlay hanya untuk tata letak satu baris (lihat OverlayTextBuilder).</summary>
    public static bool IsLatencyOnOverlayAvailable(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Layout == OverlayLayout.OneLine;
    }
}
