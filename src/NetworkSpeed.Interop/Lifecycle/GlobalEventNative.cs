using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace NetworkSpeed.Interop.Lifecycle;

/// <summary>Event bernama dengan ACL eksplisit (SDDL) dan pembukaan khusus-tunggu; dipakai untuk sinyal keluar App.</summary>
public static class GlobalEventNative
{
    private const uint Synchronize = 0x00100000;
    private const uint SddlRevision1 = 1;

    /// <summary>Membuat (atau membuka bila sudah ada) event manual-reset, awalnya non-signaled.</summary>
    /// <exception cref="Win32Exception">SDDL tidak valid atau pembuatan ditolak.</exception>
    public static SafeWaitHandle Create(string name, string sddl)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(sddl);
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, SddlRevision1, out var descriptor, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "Invalid security descriptor.");
        }

        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptor,
                InheritHandle = 0,
            };
            var handle = CreateEventW(ref attributes, manualReset: true, initialState: false, name);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw new Win32Exception(error, "Exit event could not be created.");
            }

            return handle;
        }
        finally
        {
            LocalFree(descriptor);
        }
    }

    /// <summary>Membuka event hanya dengan hak SYNCHRONIZE (tidak bisa men-set); <c>null</c> bila tidak ada/ditolak.</summary>
    public static WaitHandle? TryOpenForWait(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        var handle = OpenEventW(Synchronize, inheritHandle: false, name);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        return new OpenedWaitHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }

    private sealed class OpenedWaitHandle : WaitHandle
    {
        public OpenedWaitHandle(SafeWaitHandle handle) => SafeWaitHandle = handle;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string sddl, uint revision, out IntPtr descriptor, IntPtr size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeWaitHandle CreateEventW(ref SecurityAttributes attributes, [MarshalAs(UnmanagedType.Bool)] bool manualReset, [MarshalAs(UnmanagedType.Bool)] bool initialState, string name);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern SafeWaitHandle OpenEventW(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, string name);

    [DllImport("kernel32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
