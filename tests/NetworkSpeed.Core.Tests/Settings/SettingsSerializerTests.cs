using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.Formatting;
using NetworkSpeed.Core.Settings;
using NetworkSpeed.Core.Taskbar;

namespace NetworkSpeed.Core.Tests.Settings;

public class SettingsSerializerTests
{
    [Fact]
    public void Deserialize_EmptyObject_AllDefaults()
        => Assert.Equal(new UserSettings(), SettingsSerializer.Deserialize("{}"));

    [Fact]
    public void Load_WrongTypeForOneField_OnlyThatFieldDefaults()
    {
        var s = SettingsSerializer.Deserialize("""{"RefreshIntervalMs":"fast","FontSize":14}""");
        Assert.Equal(1000, s.RefreshIntervalMs);
        Assert.Equal(14d, s.FontSize);
    }

    [Fact]
    public void Load_UnknownField_Ignored()
        => Assert.Equal(12d, SettingsSerializer.Deserialize("""{"Foo":1,"FontSize":12}""").FontSize);

    [Fact]
    public void Deserialize_EnumsAreCaseInsensitiveNames_AndUnknownNameDefaults()
    {
        var s = SettingsSerializer.Deserialize("""{"Position":"center","UnitMode":"Furlongs","AdapterMode":5}""");
        Assert.Equal(OverlayPosition.Center, s.Position);
        Assert.Equal(SpeedUnitMode.BytesPerSecond, s.UnitMode);
        Assert.Equal(AdapterSelectionMode.Auto, s.AdapterMode);
    }

    [Fact]
    public void Deserialize_NotAnObject_Throws()
        => Assert.Throws<SettingsFormatException>(() => SettingsSerializer.Deserialize("[1,2]"));

    [Fact]
    public void Deserialize_Garbage_Throws()
        => Assert.Throws<SettingsFormatException>(() => SettingsSerializer.Deserialize("not json {"));

    [Fact]
    public void Normalize_OutOfRange_Clamped()
    {
        var s = SettingsValidator.Normalize(new UserSettings
        {
            RefreshIntervalMs = 50,
            FontSize = 99,
            OffsetRight = new PixelOffset(999, -999),
            BackgroundOpacity = 3,
            PingIntervalMs = 10,
        });
        Assert.Equal(500, s.RefreshIntervalMs);
        Assert.Equal(16d, s.FontSize);
        Assert.Equal(new PixelOffset(200, -200), s.OffsetRight);
        Assert.Equal(1d, s.BackgroundOpacity);
        Assert.Equal(1000, s.PingIntervalMs);
    }

    [Fact]
    public void Normalize_InvalidColor_BecomesDefault()
    {
        var s = SettingsValidator.Normalize(new UserSettings { UpColor = "red", AlertColor = "#12" });
        Assert.Null(s.UpColor);
        Assert.Equal("#FF4040", s.AlertColor);
    }

    [Fact]
    public void Normalize_InvalidHotkey_BecomesDefault()
        => Assert.Equal("Ctrl+Alt+N", SettingsValidator.Normalize(new UserSettings { Hotkey = "Ctrl+" }).Hotkey);

    [Fact]
    public void Normalize_InvalidTextFields_BecomeDefault()
    {
        var s = SettingsValidator.Normalize(new UserSettings
        {
            FontFamily = "  ",
            PingHost = "bad host name!",
            SpecificMonitorId = new string('x', 300),
            AlertThresholdBytesPerSecond = -1,
            FontSize = double.NaN,
        });
        Assert.Equal("Segoe UI", s.FontFamily);
        Assert.Null(s.PingHost);
        Assert.Null(s.SpecificMonitorId);
        Assert.Null(s.AlertThresholdBytesPerSecond);
        Assert.Equal(11d, s.FontSize);
    }

    [Fact]
    public void Deserialize_MissingCheckForUpdates_DefaultsTrue() =>
        Assert.True(SettingsSerializer.Deserialize("""{"SchemaVersion":1,"FontSize":12}""").CheckForUpdates);

    [Fact]
    public void Deserialize_CheckForUpdatesFalse_Read()
    {
        var s = SettingsSerializer.Deserialize("""{"SchemaVersion":1,"CheckForUpdates":false}""");
        Assert.False(s.CheckForUpdates);
        Assert.False(SettingsSerializer.Deserialize(SettingsSerializer.Serialize(s)).CheckForUpdates);
    }

    [Fact]
    public void Deserialize_CheckForUpdatesWrongType_DefaultsTrue() =>
        Assert.True(SettingsSerializer.Deserialize("""{"SchemaVersion":1,"CheckForUpdates":"no"}""").CheckForUpdates);

    [Fact]
    public void Deserialize_OldFileWithoutQuota_Defaults()
    {
        var s = SettingsSerializer.Deserialize("""{"SchemaVersion":1,"FontSize":12}""");
        Assert.Null(s.QuotaDailyGb);
        Assert.Null(s.QuotaMonthlyGb);
        Assert.Equal(1, s.QuotaMonthStartDay);
        Assert.Equal(12d, s.FontSize);
    }

    [Fact]
    public void Normalize_QuotaOutOfRange_ClampedOrNull()
    {
        var s = SettingsValidator.Normalize(new UserSettings { QuotaDailyGb = 0.01, QuotaMonthlyGb = 5_000_000, QuotaMonthStartDay = 40 });
        Assert.Equal(0.1, s.QuotaDailyGb);
        Assert.Equal(1_000_000d, s.QuotaMonthlyGb);
        Assert.Equal(31, s.QuotaMonthStartDay);
        Assert.Null(SettingsValidator.Normalize(new UserSettings { QuotaDailyGb = -1 }).QuotaDailyGb);
        Assert.Null(SettingsValidator.Normalize(new UserSettings { QuotaMonthlyGb = double.NaN }).QuotaMonthlyGb);
        Assert.Equal(1, SettingsValidator.Normalize(new UserSettings { QuotaMonthStartDay = 0 }).QuotaMonthStartDay);
    }

    [Fact]
    public void RoundTrip_QuotaFields()
    {
        var s = new UserSettings { QuotaDailyGb = 2.5, QuotaMonthlyGb = 100, QuotaMonthStartDay = 15 };
        Assert.Equal(s, SettingsSerializer.Deserialize(SettingsSerializer.Serialize(s)));
    }

    [Fact]
    public void RoundTrip_PreservesAllFields()
    {
        var original = new UserSettings
        {
            UnitMode = SpeedUnitMode.BitsPerSecond,
            RefreshIntervalMs = 2000,
            AdapterMode = AdapterSelectionMode.Specific,
            SpecificAdapterId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            IncludeVpnInAll = true,
            Position = OverlayPosition.Center,
            OffsetLeft = new PixelOffset(1, 2),
            OffsetCenter = new PixelOffset(-3, 4),
            OffsetRight = new PixelOffset(5, -6),
            Layout = OverlayLayout.OneLine,
            FontFamily = "Consolas",
            FontSize = 12.5,
            UpColor = "#00FF00",
            DownColor = "#800000FF",
            AlertColor = "#FFFFFF",
            BackgroundOpacity = 0.5,
            Theme = ThemeMode.Dark,
            AlertThresholdBytesPerSecond = 1_000_000,
            HideOnFullscreen = false,
            MonitorMode = MonitorMode.Specific,
            SpecificMonitorId = @"\\.\DISPLAY2",
            PingEnabled = false,
            PingHost = "intranet.example",
            PingIntervalMs = 10_000,
            ShowLatencyOnOverlay = true,
            ConnectivityNotifications = false,
            Hotkey = "Ctrl+Shift+F9",
            AutostartEnabled = false,
        };
        Assert.Equal(original, SettingsSerializer.Deserialize(SettingsSerializer.Serialize(original)));
    }
}
