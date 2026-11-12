using NetworkSpeed.Core.Settings;
using NetworkSpeed.Core.Startup;

namespace NetworkSpeed.Core.Tests.Startup;

public class AutostartGateTests
{
    [Theory]
    [InlineData(new[] { "--autostart" }, false, true)]
    [InlineData(new[] { "--autostart" }, true, false)]
    [InlineData(new string[0], false, false)]
    [InlineData(new[] { "--other" }, false, false)]
    [InlineData(new[] { "--AUTOSTART" }, false, true)]
    public void ShouldExit(string[] args, bool autostartEnabled, bool expected)
        => Assert.Equal(expected, AutostartGate.ShouldExit(args, new UserSettings { AutostartEnabled = autostartEnabled }));
}
