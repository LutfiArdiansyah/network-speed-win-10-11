using NetworkSpeed.Interop.Shell;
using NetworkSpeed.Interop.Wlan;

namespace NetworkSpeed.Interop.Tests.Shell;

public class ShellStateTests
{
    [Fact]
    public void ShellState_CallsDoNotThrow()
    {
        _ = ShellState.IsFullscreenAppActive();
        _ = ShellState.IsTaskbarAutoHide();
        _ = ShellState.IsLightTheme();
    }

    [Fact]
    public void WlanSsidReader_UnknownInterface_ReturnsNull()
        => Assert.Null(WlanSsidReader.TryReadSsid(Guid.NewGuid()));
}
