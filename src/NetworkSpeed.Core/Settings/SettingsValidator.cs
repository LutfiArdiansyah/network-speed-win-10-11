using System.Text.RegularExpressions;
using NetworkSpeed.Core.Input;
using NetworkSpeed.Core.Taskbar;

namespace NetworkSpeed.Core.Settings;

/// <summary>Mengembalikan setiap field yang tidak valid ke batas atau default-nya, tanpa menyentuh field lain (spec §5).</summary>
public static partial class SettingsValidator
{
    public const int MinRefreshMs = 500;
    public const int MaxRefreshMs = 5000;
    public const double MinFontSize = 8;
    public const double MaxFontSize = 16;
    public const int MaxOffset = 200;
    public const int MinPingMs = 1000;
    public const int MaxPingMs = 60000;
    public const int MaxFontFamilyLength = 64;
    public const int MaxHostLength = 253;
    public const int MaxMonitorIdLength = 256;
    public const double MinQuotaGb = 0.1;
    public const double MaxDailyQuotaGb = 100_000;
    public const double MaxMonthlyQuotaGb = 1_000_000;

    private static readonly UserSettings Defaults = new();

    public static UserSettings Normalize(UserSettings s)
    {
        ArgumentNullException.ThrowIfNull(s);

        return s with
        {
            SchemaVersion = UserSettings.CurrentSchemaVersion,
            UnitMode = Enum.IsDefined(s.UnitMode) ? s.UnitMode : Defaults.UnitMode,
            RefreshIntervalMs = Math.Clamp(s.RefreshIntervalMs, MinRefreshMs, MaxRefreshMs),
            AdapterMode = Enum.IsDefined(s.AdapterMode) ? s.AdapterMode : Defaults.AdapterMode,
            Position = Enum.IsDefined(s.Position) ? s.Position : Defaults.Position,
            OffsetLeft = Clamp(s.OffsetLeft),
            OffsetCenter = Clamp(s.OffsetCenter),
            OffsetRight = Clamp(s.OffsetRight),
            Layout = Enum.IsDefined(s.Layout) ? s.Layout : Defaults.Layout,
            FontFamily = IsValidFontFamily(s.FontFamily) ? s.FontFamily.Trim() : Defaults.FontFamily,
            FontSize = double.IsFinite(s.FontSize) ? Math.Clamp(s.FontSize, MinFontSize, MaxFontSize) : Defaults.FontSize,
            UpColor = IsValidColor(s.UpColor) ? s.UpColor : null,
            DownColor = IsValidColor(s.DownColor) ? s.DownColor : null,
            AlertColor = IsValidColor(s.AlertColor) ? s.AlertColor : Defaults.AlertColor,
            BackgroundOpacity = double.IsFinite(s.BackgroundOpacity) ? Math.Clamp(s.BackgroundOpacity, 0, 1) : Defaults.BackgroundOpacity,
            Theme = Enum.IsDefined(s.Theme) ? s.Theme : Defaults.Theme,
            AlertThresholdBytesPerSecond = s.AlertThresholdBytesPerSecond is { } t && double.IsFinite(t) && t > 0 ? t : null,
            MonitorMode = Enum.IsDefined(s.MonitorMode) ? s.MonitorMode : Defaults.MonitorMode,
            SpecificMonitorId = s.SpecificMonitorId is { Length: > 0 and <= MaxMonitorIdLength } id ? id : null,
            PingHost = IsValidHost(s.PingHost) ? s.PingHost!.Trim() : null,
            PingIntervalMs = Math.Clamp(s.PingIntervalMs, MinPingMs, MaxPingMs),
            Hotkey = HotkeyGesture.TryParse(s.Hotkey, out var gesture) ? gesture.ToString() : Defaults.Hotkey,
            QuotaDailyGb = Quota(s.QuotaDailyGb, MaxDailyQuotaGb),
            QuotaMonthlyGb = Quota(s.QuotaMonthlyGb, MaxMonthlyQuotaGb),
            QuotaMonthStartDay = Math.Clamp(s.QuotaMonthStartDay, 1, 31),
        };
    }

    // Kuota: null atau ≤ 0 = mati; selain itu di-clamp ke rentang yang wajar.
    private static double? Quota(double? value, double max) =>
        value is { } v && double.IsFinite(v) && v > 0 ? Math.Clamp(v, MinQuotaGb, max) : null;

    public static bool IsValidColor(string? color) => color is not null && ColorPattern().IsMatch(color);

    private static PixelOffset Clamp(PixelOffset o) =>
        new(Math.Clamp(o.X, -MaxOffset, MaxOffset), Math.Clamp(o.Y, -MaxOffset, MaxOffset));

    private static bool IsValidFontFamily(string? f) =>
        !string.IsNullOrWhiteSpace(f) && f.Trim().Length <= MaxFontFamilyLength;

    private static bool IsValidHost(string? host) =>
        !string.IsNullOrWhiteSpace(host)
        && host.Trim().Length <= MaxHostLength
        && Uri.CheckHostName(host.Trim()) != UriHostNameType.Unknown;

    [GeneratedRegex("^#([0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$")]
    private static partial Regex ColorPattern();
}
