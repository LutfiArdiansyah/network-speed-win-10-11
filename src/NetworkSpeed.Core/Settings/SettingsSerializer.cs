using System.Text.Json;
using System.Text.Json.Serialization;
using NetworkSpeed.Core.Taskbar;

namespace NetworkSpeed.Core.Settings;

/// <summary>File pengaturan bukan JSON object yang valid.</summary>
public sealed class SettingsFormatException : Exception
{
    public SettingsFormatException()
    {
    }

    public SettingsFormatException(string message)
        : base(message)
    {
    }

    public SettingsFormatException(string message, Exception inner)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Membaca pengaturan <b>per field</b>: field yang tipenya salah kembali ke default tanpa menggagalkan field lain,
/// dan field yang tidak dikenal diabaikan (spec §5).
/// </summary>
public static class SettingsSerializer
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(UserSettings settings) => JsonSerializer.Serialize(settings, WriteOptions);

    /// <exception cref="SettingsFormatException">Teks bukan JSON object.</exception>
    public static UserSettings Deserialize(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new SettingsFormatException("Settings file is not valid JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new SettingsFormatException("Settings file must contain a JSON object.");
            }

            var d = new UserSettings();
            var s = d with
            {
                UnitMode = Enum(root, nameof(d.UnitMode), d.UnitMode),
                RefreshIntervalMs = Int(root, nameof(d.RefreshIntervalMs), d.RefreshIntervalMs),
                AdapterMode = Enum(root, nameof(d.AdapterMode), d.AdapterMode),
                SpecificAdapterId = NullableGuid(root, nameof(d.SpecificAdapterId)),
                IncludeVpnInAll = Bool(root, nameof(d.IncludeVpnInAll), d.IncludeVpnInAll),
                Position = Enum(root, nameof(d.Position), d.Position),
                OffsetLeft = Offset(root, nameof(d.OffsetLeft)),
                OffsetCenter = Offset(root, nameof(d.OffsetCenter)),
                OffsetRight = Offset(root, nameof(d.OffsetRight)),
                Layout = Enum(root, nameof(d.Layout), d.Layout),
                FontFamily = String(root, nameof(d.FontFamily)) ?? d.FontFamily,
                FontSize = Double(root, nameof(d.FontSize)) ?? d.FontSize,
                UpColor = String(root, nameof(d.UpColor)),
                DownColor = String(root, nameof(d.DownColor)),
                AlertColor = String(root, nameof(d.AlertColor)) ?? d.AlertColor,
                BackgroundOpacity = Double(root, nameof(d.BackgroundOpacity)) ?? d.BackgroundOpacity,
                Theme = Enum(root, nameof(d.Theme), d.Theme),
                AlertThresholdBytesPerSecond = Double(root, nameof(d.AlertThresholdBytesPerSecond)),
                HideOnFullscreen = Bool(root, nameof(d.HideOnFullscreen), d.HideOnFullscreen),
                MonitorMode = Enum(root, nameof(d.MonitorMode), d.MonitorMode),
                SpecificMonitorId = String(root, nameof(d.SpecificMonitorId)),
                PingEnabled = Bool(root, nameof(d.PingEnabled), d.PingEnabled),
                PingHost = String(root, nameof(d.PingHost)),
                PingIntervalMs = Int(root, nameof(d.PingIntervalMs), d.PingIntervalMs),
                ShowLatencyOnOverlay = Bool(root, nameof(d.ShowLatencyOnOverlay), d.ShowLatencyOnOverlay),
                ConnectivityNotifications = Bool(root, nameof(d.ConnectivityNotifications), d.ConnectivityNotifications),
                Hotkey = String(root, nameof(d.Hotkey)) ?? d.Hotkey,
                AutostartEnabled = Bool(root, nameof(d.AutostartEnabled), d.AutostartEnabled),
                QuotaDailyGb = Double(root, nameof(d.QuotaDailyGb)),
                QuotaMonthlyGb = Double(root, nameof(d.QuotaMonthlyGb)),
                QuotaMonthStartDay = Int(root, nameof(d.QuotaMonthStartDay), d.QuotaMonthStartDay),
                CheckForUpdates = Bool(root, nameof(d.CheckForUpdates), d.CheckForUpdates),
            };
            return SettingsValidator.Normalize(s);
        }
    }

    private static bool TryGet(JsonElement root, string name, JsonValueKind kind, out JsonElement value) =>
        root.TryGetProperty(name, out value) && value.ValueKind == kind;

    private static int Int(JsonElement root, string name, int fallback) =>
        TryGet(root, name, JsonValueKind.Number, out var v) && v.TryGetInt32(out var i) ? i : fallback;

    private static double? Double(JsonElement root, string name) =>
        TryGet(root, name, JsonValueKind.Number, out var v) && v.TryGetDouble(out var x) ? x : null;

    private static bool Bool(JsonElement root, string name, bool fallback) =>
        root.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : fallback;

    private static string? String(JsonElement root, string name) =>
        TryGet(root, name, JsonValueKind.String, out var v) ? v.GetString() : null;

    private static Guid? NullableGuid(JsonElement root, string name) =>
        String(root, name) is { } text && Guid.TryParse(text, out var g) ? g : null;

    // Enum hanya dibaca dari nama (bukan angka) agar nilai di luar enum tidak lolos.
    private static T Enum<T>(JsonElement root, string name, T fallback)
        where T : struct, Enum =>
        String(root, name) is { } text
        && System.Enum.TryParse<T>(text, ignoreCase: true, out var value)
        && System.Enum.IsDefined(value)
        && !int.TryParse(text, out _)
            ? value
            : fallback;

    private static PixelOffset Offset(JsonElement root, string name) =>
        TryGet(root, name, JsonValueKind.Object, out var v)
            ? new PixelOffset(Int(v, nameof(PixelOffset.X), 0), Int(v, nameof(PixelOffset.Y), 0))
            : default;
}
