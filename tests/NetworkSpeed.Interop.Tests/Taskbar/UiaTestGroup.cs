namespace NetworkSpeed.Interop.Tests.Taskbar;

/// <summary>
/// Test yang memakai UI Automation taskbar tidak boleh berjalan paralel: panggilan UIA bersamaan saat start
/// bisa gagal sesaat (COMException), sama seperti yang ditangani retry di aplikasi.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class UiaTestGroup
{
    public const string Name = "Taskbar UIA";
}
