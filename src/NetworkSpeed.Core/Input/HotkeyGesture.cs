using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace NetworkSpeed.Core.Input;

/// <summary>Modifier hotkey; nilainya sama dengan konstanta Win32 <c>MOD_*</c> untuk <c>RegisterHotKey</c>.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>Kombinasi tombol global Show/Hide (spec X-03), mis. <c>Ctrl+Alt+N</c>.</summary>
public sealed record HotkeyGesture(HotkeyModifiers Modifiers, uint VirtualKey)
{
    private const uint VirtualKeyA = 0x41;
    private const uint VirtualKey0 = 0x30;
    private const uint VirtualKeyF1 = 0x70;
    private const int MaxFunctionKey = 24;

    private static readonly (string Name, HotkeyModifiers Flag)[] ModifierNames =
    [
        ("Ctrl", HotkeyModifiers.Ctrl),
        ("Alt", HotkeyModifiers.Alt),
        ("Shift", HotkeyModifiers.Shift),
        ("Win", HotkeyModifiers.Win),
    ];

    /// <summary>
    /// Mem-parsing teks seperti <c>Ctrl+Alt+N</c> (tidak peka huruf besar/kecil). Valid bila ada minimal satu
    /// modifier selain Shift dan tombol utama A–Z, 0–9, atau F1–F24 di posisi terakhir.
    /// </summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out HotkeyGesture? gesture)
    {
        gesture = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var tokens = text.Split('+').Select(t => t.Trim()).ToArray();
        if (tokens.Any(t => t.Length == 0))
        {
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        foreach (var token in tokens[..^1])
        {
            var flag = ParseModifier(token);
            if (flag == HotkeyModifiers.None || modifiers.HasFlag(flag))
            {
                return false;
            }

            modifiers |= flag;
        }

        if ((modifiers & ~HotkeyModifiers.Shift) == HotkeyModifiers.None || !TryParseKey(tokens[^1], out var vk))
        {
            return false;
        }

        gesture = new HotkeyGesture(modifiers, vk);
        return true;
    }

    public override string ToString()
    {
        var parts = ModifierNames.Where(m => Modifiers.HasFlag(m.Flag)).Select(m => m.Name).ToList();
        parts.Add(KeyName(VirtualKey));
        return string.Join('+', parts);
    }

    private static HotkeyModifiers ParseModifier(string token) => token.ToUpperInvariant() switch
    {
        "CTRL" or "CONTROL" => HotkeyModifiers.Ctrl,
        "ALT" => HotkeyModifiers.Alt,
        "SHIFT" => HotkeyModifiers.Shift,
        "WIN" => HotkeyModifiers.Win,
        _ => HotkeyModifiers.None,
    };

    private static bool TryParseKey(string token, out uint vk)
    {
        vk = 0;
        var upper = token.ToUpperInvariant();
        if (upper.Length == 1 && upper[0] is >= 'A' and <= 'Z')
        {
            vk = VirtualKeyA + (uint)(upper[0] - 'A');
            return true;
        }

        if (upper.Length == 1 && upper[0] is >= '0' and <= '9')
        {
            vk = VirtualKey0 + (uint)(upper[0] - '0');
            return true;
        }

        if (upper.Length >= 2 && upper[0] == 'F'
            && int.TryParse(upper.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n)
            && n is >= 1 and <= MaxFunctionKey)
        {
            vk = VirtualKeyF1 + (uint)(n - 1);
            return true;
        }

        return false;
    }

    private static string KeyName(uint vk) => vk switch
    {
        >= VirtualKeyF1 and < VirtualKeyF1 + MaxFunctionKey => "F" + (vk - VirtualKeyF1 + 1).ToString(CultureInfo.InvariantCulture),
        _ => ((char)vk).ToString(),
    };
}
