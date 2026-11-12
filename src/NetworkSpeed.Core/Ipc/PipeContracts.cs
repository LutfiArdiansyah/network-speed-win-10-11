using System.Text.Json.Serialization;
using NetworkSpeed.Core.Adapters;
using NetworkSpeed.Core.History;
using NetworkSpeed.Core.PerApp;

namespace NetworkSpeed.Core.Ipc;

/// <summary>Resolusi riwayat (spec H-07).</summary>
public enum HistoryResolution
{
    Minute,
    Day,
}

/// <summary>Periode laporan per aplikasi (spec S-02).</summary>
public enum PerAppPeriod
{
    Session,
    Today,
}

/// <summary>Nama operasi pipe; semuanya query read-only (spec S-04).</summary>
public static class PipeOperations
{
    public const string GetStatus = nameof(GetStatus);
    public const string GetAdapters = nameof(GetAdapters);
    public const string GetHistory = nameof(GetHistory);
    public const string GetDailyTotals = nameof(GetDailyTotals);
    public const string GetPerApp = nameof(GetPerApp);

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        GetStatus, GetAdapters, GetHistory, GetDailyTotals, GetPerApp,
    };
}

/// <summary>Kode error ke klien; tidak pernah memuat detail internal (spec S-08).</summary>
public static class PipeErrors
{
    public const string UnsupportedVersion = "unsupported_version";
    public const string InvalidRequest = "invalid_request";
    public const string TooLarge = "too_large";
    public const string Unavailable = "unavailable";
    public const string Internal = "internal";
}

/// <summary>Permintaan dari App ke service.</summary>
public sealed record PipeRequest(int ProtocolVersion, string Operation)
{
    public IReadOnlyList<Guid>? AdapterIds { get; init; }

    /// <summary>Waktu lokal <c>yyyy-MM-ddTHH:mm</c> (menit) atau <c>yyyy-MM-dd</c> (hari), inklusif.</summary>
    public string? From { get; init; }

    public string? To { get; init; }

    public HistoryResolution? Resolution { get; init; }

    public PerAppPeriod? Period { get; init; }
}

/// <summary>Status service.</summary>
public sealed record ServiceStatus(string Version, long UptimeSeconds, bool EtwAvailable);

/// <summary>Satu baris laporan per aplikasi; angka sudah diskalakan ke total adapter (spec S-09, "approximate").</summary>
public sealed record PerAppRow(string Name, bool IsSystem, ulong SentBytes, ulong ReceivedBytes);

/// <summary>Jawaban service. Tepat satu payload terisi bila <see cref="Ok"/>.</summary>
public sealed record PipeResponse(int ProtocolVersion, bool Ok)
{
    public string? ErrorCode { get; init; }

    public ServiceStatus? Status { get; init; }

    public IReadOnlyList<AdapterInfo>? Adapters { get; init; }

    public IReadOnlyList<HistoryPoint>? History { get; init; }

    public IReadOnlyList<PerAppRow>? PerApp { get; init; }

    public static PipeResponse Error(string code) => new(PipeProtocol.Version, false) { ErrorCode = code };

    public static PipeResponse Success() => new(PipeProtocol.Version, true);
}

/// <summary>Serializer JSON hasil source generator (tanpa reflection), enum sebagai teks.</summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PipeRequest))]
[JsonSerializable(typeof(PipeResponse))]
internal sealed partial class PipeJsonContext : JsonSerializerContext;
