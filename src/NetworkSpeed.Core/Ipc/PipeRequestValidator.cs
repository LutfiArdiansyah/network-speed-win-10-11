using System.Globalization;
using NetworkSpeed.Core.History;

namespace NetworkSpeed.Core.Ipc;

/// <summary>Validasi semua request pipe sebelum diproses (spec S-08): versi, operasi, jumlah adapter, format & rentang waktu.</summary>
public static class PipeRequestValidator
{
    /// <returns><c>null</c> bila valid; selain itu kode <see cref="PipeErrors"/>.</returns>
    public static string? Validate(PipeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.ProtocolVersion != PipeProtocol.Version)
        {
            return PipeErrors.UnsupportedVersion;
        }

        return request.Operation switch
        {
            PipeOperations.GetStatus or PipeOperations.GetAdapters => null,
            PipeOperations.GetHistory => ValidateHistory(request, request.Resolution),
            PipeOperations.GetDailyTotals => ValidateHistory(request, HistoryResolution.Day),
            PipeOperations.GetPerApp => request.Period is { } p && Enum.IsDefined(p) ? null : PipeErrors.InvalidRequest,
            _ => PipeErrors.InvalidRequest,
        };
    }

    private static string? ValidateHistory(PipeRequest r, HistoryResolution? resolution)
    {
        if (resolution is not { } res || !Enum.IsDefined(res)
            || r.AdapterIds is not { Count: > 0 and <= PipeProtocol.MaxAdapterIds })
        {
            return PipeErrors.InvalidRequest;
        }

        var format = res == HistoryResolution.Minute ? LocalTimeKey.MinuteFormat : LocalTimeKey.DayFormat;
        if (!TryParse(r.From, format, out var from) || !TryParse(r.To, format, out var to) || from > to)
        {
            return PipeErrors.InvalidRequest;
        }

        var points = res == HistoryResolution.Minute ? (to - from).TotalMinutes + 1 : (to - from).TotalDays + 1;
        var max = res == HistoryResolution.Minute ? PipeProtocol.MaxHistoryPoints : PipeProtocol.MaxHistoryDays;
        return points <= max ? null : PipeErrors.InvalidRequest;
    }

    private static bool TryParse(string? text, string format, out DateTime value) =>
        DateTime.TryParseExact(text, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
}
