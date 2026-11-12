using NetworkSpeed.Core.Input;

namespace NetworkSpeed.Core.Tests.Input;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+N", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x4Eu)]
    [InlineData("ctrl+shift+F12", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, 0x7Bu)]
    [InlineData("Win+Alt+5", HotkeyModifiers.Win | HotkeyModifiers.Alt, 0x35u)]
    [InlineData(" Alt + F1 ", HotkeyModifiers.Alt, 0x70u)]
    public void TryParse_ValidGestures(string text, HotkeyModifiers modifiers, uint vk)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Equal(modifiers, gesture!.Modifiers);
        Assert.Equal(vk, gesture.VirtualKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("N")]
    [InlineData("Shift+N")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt+Foo")]
    [InlineData("Ctrl+Ctrl+N")]
    [InlineData("Ctrl+N+Alt")]
    [InlineData("Ctrl+F25")]
    [InlineData(null)]
    public void TryParse_InvalidGestures_ReturnFalse(string? text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out var gesture));
        Assert.Null(gesture);
    }

    [Theory]
    [InlineData("alt+ctrl+n", "Ctrl+Alt+N")]
    [InlineData("Shift+Win+Ctrl+F3", "Ctrl+Shift+Win+F3")]
    public void ToString_IsCanonicalAndRoundTrips(string input, string expected)
    {
        Assert.True(HotkeyGesture.TryParse(input, out var gesture));
        Assert.Equal(expected, gesture!.ToString());
        Assert.True(HotkeyGesture.TryParse(gesture.ToString(), out var again));
        Assert.Equal(gesture, again);
    }
}
