namespace NetworkSpeed.Core.Taskbar;

/// <summary>Sumber layout taskbar (implementasi Win32/UIA ada di NetworkSpeed.Interop).</summary>
public interface ITaskbarLayoutReader
{
    /// <summary>Layout semua taskbar. Mahal (UIA); panggil dari thread background, bukan tiap polling.</summary>
    IReadOnlyList<TaskbarLayout> ReadAll();

    /// <summary>Rect Win32 taskbar + area tray; murah, untuk deteksi perubahan (spec T-05).</summary>
    IReadOnlyList<PixelRect> ReadWin32Rects();

    /// <summary>Handle jendela taskbar utama (0 bila Explorer tidak berjalan); berubah setelah Explorer restart.</summary>
    nint PrimaryTaskbarHandle();
}
