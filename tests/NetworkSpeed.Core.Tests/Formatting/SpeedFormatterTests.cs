using System.Globalization;
using NetworkSpeed.Core.Formatting;

namespace NetworkSpeed.Core.Tests.Formatting;

public class SpeedFormatterTests
{
    [Theory]
    [InlineData(0d, "0.0 KB/s")]
    [InlineData(512d, "0.5 KB/s")]
    [InlineData(1024d * 9.94, "9.9 KB/s")]
    [InlineData(1024d * 9.96, "10 KB/s")]
    [InlineData(1024d * 512, "512 KB/s")]
    [InlineData(1024d * 999.6, "1.0 MB/s")]
    [InlineData(1024d * 1023.99, "1.0 MB/s")]
    [InlineData(1024d * 1024 * 5.43, "5.4 MB/s")]
    [InlineData(1024d * 1024 * 1024 * 1.25, "1.3 GB/s")]
    public void Format_Bytes(double bytesPerSecond, string expected)
        => Assert.Equal(expected, SpeedFormatter.Format(bytesPerSecond, SpeedUnitMode.BytesPerSecond));

    [Theory]
    [InlineData(0d, "0.0 Kbps")]
    [InlineData(125d, "1.0 Kbps")]
    [InlineData(124_995d, "1.0 Mbps")]
    [InlineData(1_250_000d, "10 Mbps")]
    [InlineData(125_000_000d * 10, "10 Gbps")]
    public void Format_Bits(double bytesPerSecond, string expected)
        => Assert.Equal(expected, SpeedFormatter.Format(bytesPerSecond, SpeedUnitMode.BitsPerSecond));

    [Theory]
    [InlineData(-5d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Format_InvalidInput_TreatedAsZero(double bytesPerSecond)
        => Assert.Equal("0.0 KB/s", SpeedFormatter.Format(bytesPerSecond, SpeedUnitMode.BytesPerSecond));

    [Fact]
    public void Format_HugeValue_StaysInLargestUnit()
        => Assert.Equal("2048 TB/s", SpeedFormatter.Format(1024d * 1024 * 1024 * 1024 * 2048, SpeedUnitMode.BytesPerSecond));

    [Fact]
    public void Format_UnderIndonesianCulture_UsesDot()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("id-ID");
            Assert.Equal("5.4 MB/s", SpeedFormatter.Format(1024d * 1024 * 5.43, SpeedUnitMode.BytesPerSecond));
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }
}
