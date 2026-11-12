namespace NetworkSpeed.Core.Display;

/// <summary>Skala sumbu Y grafik.</summary>
public static class ChartScale
{
    private static readonly double[] Multipliers = [1, 2, 5, 10];

    /// <summary>Batas atas "rapi" (1, 2, atau 5 × 10ⁿ) yang ≥ <paramref name="max"/>; nilai ≤ 0 atau tidak valid → 1.</summary>
    public static double NiceMax(double max)
    {
        if (!double.IsFinite(max) || max <= 0)
        {
            return 1;
        }

        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(max)));
        foreach (var multiplier in Multipliers)
        {
            var candidate = multiplier * magnitude;
            if (candidate >= max * (1 - 1e-12))
            {
                return candidate;
            }
        }

        return 10 * magnitude;
    }
}
