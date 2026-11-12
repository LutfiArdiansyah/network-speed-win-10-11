using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.Formatting;
using NetworkSpeed.Core.Taskbar;

namespace NetworkSpeed.Core.Settings;

/// <summary>Tata letak teks overlay (spec D-03).</summary>
public enum OverlayLayout
{
    OneLine,
    TwoLines,
}

/// <summary>Sumber tema warna overlay (spec D-01).</summary>
public enum ThemeMode
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Pengaturan per user (<c>%LOCALAPPDATA%\NetworkSpeed\settings.json</c>, spec §5).
/// Ini kontrak format data: field baru wajib punya default; field yang dihapus tetap diabaikan saat dibaca.
/// </summary>
public sealed record UserSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public SpeedUnitMode UnitMode { get; init; } = SpeedUnitMode.BytesPerSecond;

    public int RefreshIntervalMs { get; init; } = 1000;

    public AdapterSelectionMode AdapterMode { get; init; } = AdapterSelectionMode.Auto;

    public Guid? SpecificAdapterId { get; init; }

    public bool IncludeVpnInAll { get; init; }

    public OverlayPosition Position { get; init; } = OverlayPosition.Right;

    public PixelOffset OffsetLeft { get; init; }

    public PixelOffset OffsetCenter { get; init; }

    public PixelOffset OffsetRight { get; init; }

    public OverlayLayout Layout { get; init; } = OverlayLayout.TwoLines;

    public string FontFamily { get; init; } = "Segoe UI";

    public double FontSize { get; init; } = 11;

    public string? UpColor { get; init; }

    public string? DownColor { get; init; }

    public string AlertColor { get; init; } = "#FF4040";

    public double BackgroundOpacity { get; init; }

    public ThemeMode Theme { get; init; } = ThemeMode.System;

    public double? AlertThresholdBytesPerSecond { get; init; }

    public bool HideOnFullscreen { get; init; } = true;

    public MonitorMode MonitorMode { get; init; } = MonitorMode.PrimaryOnly;

    public string? SpecificMonitorId { get; init; }

    public bool PingEnabled { get; init; } = true;

    public string? PingHost { get; init; }

    public int PingIntervalMs { get; init; } = 5000;

    public bool ShowLatencyOnOverlay { get; init; }

    public bool ConnectivityNotifications { get; init; } = true;

    public string Hotkey { get; init; } = "Ctrl+Alt+N";

    public bool AutostartEnabled { get; init; } = true;

    /// <summary>Batas pemakaian harian dalam GB (1 GB = 1024³ byte); null = kuota harian mati (spec H-01).</summary>
    public double? QuotaDailyGb { get; init; }

    /// <summary>Batas pemakaian bulanan dalam GB; null = kuota bulanan mati.</summary>
    public double? QuotaMonthlyGb { get; init; }

    /// <summary>Tanggal mulai periode bulanan (1–31).</summary>
    public int QuotaMonthStartDay { get; init; } = 1;

    /// <summary>Cek versi baru di shared folder IT (spec §10); hanya informasi, per user.</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>Offset milik posisi tertentu (spec T-06).</summary>
    public PixelOffset OffsetFor(OverlayPosition position) => position switch
    {
        OverlayPosition.Left => OffsetLeft,
        OverlayPosition.Center => OffsetCenter,
        _ => OffsetRight,
    };
}
