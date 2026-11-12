using System.Globalization;

namespace NetworkSpeed.Core.Formatting;

/// <summary>
/// Memformat kecepatan menjadi teks pendek dengan lebar stabil, mis. <c>5.4 MB/s</c> atau <c>512 Kbps</c>.
/// Selalu memakai <see cref="CultureInfo.InvariantCulture"/> karena UI berbahasa Inggris (spec C-07).
/// </summary>
public static class SpeedFormatter
{
    // Mengikuti Task Manager: byte memakai skala 1024, bit memakai skala 1000 (spec F-02).
    private const double ByteScale = 1024d;
    private const double BitScale = 1000d;
    private const int BitsPerByte = 8;

    // Batas jumlah digit: di atas ini naik satu satuan, supaya teks maksimal 3 digit.
    private const double UnitOverflowThreshold = 1000d;
    private const double OneDecimalBelow = 10d;

    private static readonly string[] ByteUnits = ["KB/s", "MB/s", "GB/s", "TB/s"];
    private static readonly string[] BitUnits = ["Kbps", "Mbps", "Gbps", "Tbps"];

    /// <summary>Memformat <paramref name="bytesPerSecond"/>; nilai negatif, NaN, atau tak hingga dianggap 0.</summary>
    public static string Format(double bytesPerSecond, SpeedUnitMode mode)
    {
        var safeBytes = double.IsFinite(bytesPerSecond) && bytesPerSecond > 0 ? bytesPerSecond : 0d;
        var (value, scale, units) = mode == SpeedUnitMode.BitsPerSecond
            ? (safeBytes * BitsPerByte, BitScale, BitUnits)
            : (safeBytes, ByteScale, ByteUnits);

        value /= scale;
        var unitIndex = 0;
        while (true)
        {
            var (display, decimals) = RoundForDisplay(value);
            var isLargestUnit = unitIndex == units.Length - 1;
            if (display < UnitOverflowThreshold || isLargestUnit)
            {
                var pattern = decimals == 1 ? "0.0" : "0";
                return $"{display.ToString(pattern, CultureInfo.InvariantCulture)} {units[unitIndex]}";
            }

            value /= scale;
            unitIndex++;
        }
    }

    // Jumlah desimal ditentukan dari nilai yang sudah dibulatkan, supaya 9.96 tampil "10", bukan "10.0".
    private static (double Display, int Decimals) RoundForDisplay(double value)
    {
        var oneDecimal = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        return oneDecimal < OneDecimalBelow
            ? (oneDecimal, 1)
            : (Math.Round(value, 0, MidpointRounding.AwayFromZero), 0);
    }
}
