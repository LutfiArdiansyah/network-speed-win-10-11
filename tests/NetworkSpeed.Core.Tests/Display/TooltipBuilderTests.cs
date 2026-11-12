using NetworkSpeed.Core.Display;

namespace NetworkSpeed.Core.Tests.Display;

public class TooltipBuilderTests
{
    [Fact]
    public void TooltipBuilder_AllFields()
    {
        var data = new TooltipData("Wi-Fi", "OfficeNet", "192.168.1.20", 5UL * 1024 * 1024, 1536UL * 1024 * 1024, new LatencyReading(TimeSpan.FromMilliseconds(12)), null);
        var text = TooltipBuilder.Build(data);
        Assert.Equal(
            string.Join(Environment.NewLine, "Adapter: Wi-Fi", "SSID: OfficeNet", "IPv4: 192.168.1.20", "Session: ↑ 5.0 MB  ↓ 1.5 GB", "Latency: 12 ms"),
            text);
    }

    [Fact]
    public void TooltipBuilder_NullFieldsOmitted()
    {
        var text = TooltipBuilder.Build(new TooltipData(null, null, null, 0, 512, null, null));
        Assert.Equal("Session: ↑ 0 B  ↓ 512 B", text);
    }

    [Fact]
    public void TooltipBuilder_HiddenReasonShown()
    {
        var text = TooltipBuilder.Build(new TooltipData("Ethernet", null, null, 0, 0, null, "Not enough space on the taskbar"));
        Assert.EndsWith("Overlay hidden: Not enough space on the taskbar", text, StringComparison.Ordinal);
    }
}
