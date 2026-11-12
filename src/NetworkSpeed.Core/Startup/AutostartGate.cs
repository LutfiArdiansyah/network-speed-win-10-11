using NetworkSpeed.Core.Settings;

namespace NetworkSpeed.Core.Startup;

/// <summary>
/// Autostart dipasang per PC (HKLM Run) oleh installer, sehingga user biasa tidak bisa menghapusnya.
/// Bila user mematikan autostart, App yang dijalankan dengan <c>--autostart</c> langsung keluar (spec §6).
/// </summary>
public static class AutostartGate
{
    public const string AutostartArgument = "--autostart";

    public static bool ShouldExit(IReadOnlyList<string> args, UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(settings);
        return !settings.AutostartEnabled && args.Any(a => string.Equals(a, AutostartArgument, StringComparison.OrdinalIgnoreCase));
    }
}
