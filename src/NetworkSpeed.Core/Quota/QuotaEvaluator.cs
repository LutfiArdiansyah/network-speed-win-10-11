using System.Globalization;

namespace NetworkSpeed.Core.Quota;

/// <summary>
/// Satu ambang kuota yang baru tercapai. <see cref="IsShown"/> false berarti ambang ini hanya ditandai terkirim
/// (mis. 80% saat pemakaian langsung melompat melewati 100%), tanpa notifikasi sendiri.
/// </summary>
public sealed record QuotaAlert(QuotaKind Kind, int Percent, ulong UsedBytes, ulong LimitBytes, string StateKey, bool IsShown);

/// <summary>
/// Menentukan notifikasi kuota (spec H-01): ambang 80% dan 100%, masing-masing sekali per (periode, batas).
/// Kunci state memuat nilai batas, sehingga mengubah batas di tengah periode mengaktifkan notifikasi lagi.
/// </summary>
public static class QuotaEvaluator
{
    private static readonly int[] Thresholds = [80, 100];

    public static IReadOnlyList<QuotaAlert> Evaluate(QuotaWindow window, ulong usedBytes, ulong limitBytes, IReadOnlySet<string> alreadyNotified)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(alreadyNotified);
        if (limitBytes == 0)
        {
            return [];
        }

        var reached = Thresholds
            .Where(t => (UInt128)usedBytes * 100 >= (UInt128)limitBytes * (ulong)t)
            .Select(t => (Percent: t, Key: string.Create(CultureInfo.InvariantCulture, $"{window.Key}:{limitBytes}:{t}")))
            .Where(t => !alreadyNotified.Contains(t.Key))
            .ToList();
        if (reached.Count == 0)
        {
            return [];
        }

        var highest = reached.Max(t => t.Percent);
        return reached.Select(t => new QuotaAlert(window.Kind, t.Percent, usedBytes, limitBytes, t.Key, t.Percent == highest)).ToList();
    }
}
