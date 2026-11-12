using System.IO;
using System.Security;
using Microsoft.Win32;

namespace NetworkSpeed.App.Platform;

/// <summary>
/// Pengaturan tingkat PC di <c>HKLM\SOFTWARE\NetworkSpeed</c> (spec §5): diisi installer, hanya admin yang bisa mengubah;
/// App hanya membaca dan menampilkannya read-only.
/// </summary>
public static class MachineSettings
{
    private const string KeyPath = @"SOFTWARE\NetworkSpeed";
    private const string UpdateFolderValue = "UpdateFolder";

    /// <summary>Nilai mentah (belum divalidasi); <c>null</c> bila tidak ada atau tidak bisa dibaca.</summary>
    public static string? UpdateFolder()
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = root.OpenSubKey(KeyPath);
            return key?.GetValue(UpdateFolderValue) as string;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }
}
