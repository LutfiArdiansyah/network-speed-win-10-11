namespace NetworkSpeed.Core.Lifecycle;

/// <summary>
/// Event bernama yang dipakai installer untuk menutup App di semua sesi dengan sopan saat upgrade/uninstall (spec §8).
/// Dibuat oleh service (LocalService boleh membuat objek <c>Global\</c>; user biasa tidak, sehingga nama tidak bisa
/// diserobot). Hanya SYSTEM/Administrators/LocalService yang boleh men-set; user hanya boleh menunggu (SYNCHRONIZE).
/// </summary>
public static class AppExitSignal
{
    public const string Name = @"Global\NetworkSpeed.AppExit";

    /// <summary>
    /// SY/BA/LS: akses penuh (LS wajib: service yang restart setelah crash membuka ulang objek yang masih dipegang App).
    /// AU: hanya SYNCHRONIZE (0x00100000).
    /// </summary>
    public const string Sddl = "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GA;;;LS)(A;;0x00100000;;;AU)";
}
