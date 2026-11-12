using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using NetworkSpeed.Interop.Lifecycle;

namespace NetworkSpeed.Interop.Tests;

public class GlobalEventNativeTests
{
    private const int ErrorAccessDenied = 5;

    // Nama Local\ agar test tidak butuh hak membuat objek Global; SDDL memberi user test akses penuh.
    private static string NewName() => @"Local\NetworkSpeed.Test." + Guid.NewGuid().ToString("N");

    private static string OwnerSddl() => $"D:P(A;;GA;;;{WindowsIdentity.GetCurrent().User})";

    [Fact]
    public void Create_ThenOpenForWait_SetObservedByWaiter()
    {
        var name = NewName();
        using var created = GlobalEventNative.Create(name, OwnerSddl());
        using var waiter = GlobalEventNative.TryOpenForWait(name);
        Assert.NotNull(waiter);
        Assert.False(waiter.WaitOne(0));
        Assert.True(SetEvent(created));
        Assert.True(waiter.WaitOne(1000));
        Assert.True(waiter.WaitOne(0)); // manual-reset: tetap signaled untuk semua App.
    }

    [Fact]
    public void OpenForWait_HandleCannotSet()
    {
        var name = NewName();
        using var created = GlobalEventNative.Create(name, OwnerSddl());
        using var waiter = GlobalEventNative.TryOpenForWait(name);
        Assert.NotNull(waiter);
        Assert.False(SetEvent(waiter.SafeWaitHandle));
        Assert.Equal(ErrorAccessDenied, Marshal.GetLastPInvokeError());
    }

    [Fact]
    public void TryOpenForWait_Missing_ReturnsNull() => Assert.Null(GlobalEventNative.TryOpenForWait(NewName()));

    [Fact]
    public void Create_Twice_WhileFirstHandleOpen_Succeeds()
    {
        // Simulasi service restart saat App masih memegang handle: objek lama dibuka ulang, bukan error.
        var name = NewName();
        using var first = GlobalEventNative.Create(name, OwnerSddl());
        using var second = GlobalEventNative.Create(name, OwnerSddl());
        Assert.False(second.IsInvalid);
    }

    [Fact]
    public void Create_InvalidSddl_Throws() =>
        Assert.ThrowsAny<Exception>(() => GlobalEventNative.Create(NewName(), "not-sddl").Dispose());

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetEvent(SafeWaitHandle handle);
}
