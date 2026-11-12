using System.Globalization;

namespace NetworkSpeed.Core.Viewer;

/// <summary>Jendela yang dijalankan di proses viewer terpisah (spec §1: memori overlay tetap kecil).</summary>
public enum ViewerKind
{
    History,
    PerApp,
}

/// <summary>Argumen proses viewer: jenis jendela, PID App utama (viewer keluar bila induk keluar), dan data Live awal.</summary>
public sealed record ViewerArguments(ViewerKind Kind, int ParentProcessId, IReadOnlyList<(double Up, double Down)> Live);

/// <summary>
/// Format baris perintah viewer: <c>--viewer history|perapp --parent &lt;pid&gt; [--live up:down;…]</c>.
/// Jenis/PID tidak valid → bukan viewer yang sah (null). Data Live tidak valid atau terlalu besar dibuang (tab Live
/// mulai kosong), karena hanya pelengkap tampilan.
/// </summary>
public static class ViewerCommandLine
{
    /// <summary>Sama dengan kapasitas <see cref="Display.LiveHistoryBuffer"/>.</summary>
    public const int MaxLiveSamples = 60;

    private const int MaxLiveArgumentLength = 4096;
    private const string ViewerFlag = "--viewer";
    private const string ParentFlag = "--parent";
    private const string LiveFlag = "--live";
    private const string HistoryName = "history";
    private const string PerAppName = "perapp";

    public static bool IsViewer(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return args.Count > 0 && string.Equals(args[0], ViewerFlag, StringComparison.Ordinal);
    }

    public static ViewerArguments? Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (!IsViewer(args) || args.Count < 2)
        {
            return null;
        }

        ViewerKind? kind = args[1] switch
        {
            HistoryName => ViewerKind.History,
            PerAppName => ViewerKind.PerApp,
            _ => null,
        };
        var parent = ValueOf(args, ParentFlag);
        if (kind is null
            || !int.TryParse(parent, NumberStyles.None, CultureInfo.InvariantCulture, out var parentId)
            || parentId <= 0)
        {
            return null;
        }

        return new ViewerArguments(kind.Value, parentId, ParseLive(ValueOf(args, LiveFlag)));
    }

    public static IReadOnlyList<string> Build(ViewerArguments arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var result = new List<string>
        {
            ViewerFlag,
            arguments.Kind == ViewerKind.History ? HistoryName : PerAppName,
            ParentFlag,
            arguments.ParentProcessId.ToString(CultureInfo.InvariantCulture),
        };
        if (arguments.Live.Count > 0)
        {
            var samples = arguments.Live.Skip(Math.Max(0, arguments.Live.Count - MaxLiveSamples))
                .Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.Up:R}:{s.Down:R}"));
            result.Add(LiveFlag);
            result.Add(string.Join(';', samples));
        }

        return result;
    }

    private static string? ValueOf(IReadOnlyList<string> args, string flag)
    {
        for (var i = 2; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static List<(double Up, double Down)> ParseLive(string? text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxLiveArgumentLength)
        {
            return [];
        }

        var samples = new List<(double Up, double Down)>();
        foreach (var part in text.Split(';'))
        {
            var pair = part.Split(':');
            if (pair.Length != 2 || !TryParseRate(pair[0], out var up) || !TryParseRate(pair[1], out var down))
            {
                return [];
            }

            samples.Add((up, down));
        }

        return samples.Count > MaxLiveSamples ? samples[^MaxLiveSamples..] : samples;
    }

    private static bool TryParseRate(string text, out double value) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        && double.IsFinite(value)
        && value >= 0;
}
