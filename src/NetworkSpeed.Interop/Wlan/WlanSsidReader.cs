using System.Runtime.InteropServices;
using System.Text;

namespace NetworkSpeed.Interop.Wlan;

/// <summary>
/// Membaca SSID adapter Wi-Fi untuk tooltip (spec F-06). Semua kegagalan menghasilkan <c>null</c>:
/// bukan Wi-Fi, tidak terhubung, atau (Windows 11 24H2+) izin lokasi tidak diberikan.
/// SSID tidak boleh dicatat ke log (spec §7).
/// </summary>
public static partial class WlanSsidReader
{
    private const uint ClientVersion = 2;
    private const int OpcodeCurrentConnection = 7;

    // Offset di WLAN_CONNECTION_ATTRIBUTES: isState(4) + wlanConnectionMode(4) + strProfileName(WCHAR[256]).
    private const int SsidLengthOffset = 4 + 4 + (256 * 2);
    private const int SsidBytesOffset = SsidLengthOffset + 4;
    private const int MaxSsidLength = 32;

    public static string? TryReadSsid(Guid interfaceId)
    {
        if (WlanOpenHandle(ClientVersion, IntPtr.Zero, out _, out var handle) != 0)
        {
            return null;
        }

        var data = IntPtr.Zero;
        try
        {
            if (WlanQueryInterface(handle, ref interfaceId, OpcodeCurrentConnection, IntPtr.Zero, out var size, out data, out _) != 0
                || data == IntPtr.Zero
                || size < SsidBytesOffset + MaxSsidLength)
            {
                return null;
            }

            var length = Marshal.ReadInt32(data, SsidLengthOffset);
            if (length is <= 0 or > MaxSsidLength)
            {
                return null;
            }

            var bytes = new byte[length];
            Marshal.Copy(data + SsidBytesOffset, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            if (data != IntPtr.Zero)
            {
                WlanFreeMemory(data);
            }

            _ = WlanCloseHandle(handle, IntPtr.Zero);
        }
    }

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanOpenHandle(uint clientVersion, IntPtr reserved, out uint negotiatedVersion, out IntPtr clientHandle);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanCloseHandle(IntPtr clientHandle, IntPtr reserved);

    [LibraryImport("wlanapi.dll")]
    private static partial uint WlanQueryInterface(
        IntPtr clientHandle, ref Guid interfaceGuid, int opCode, IntPtr reserved, out uint dataSize, out IntPtr data, out int opcodeValueType);

    [LibraryImport("wlanapi.dll")]
    private static partial void WlanFreeMemory(IntPtr memory);
}
