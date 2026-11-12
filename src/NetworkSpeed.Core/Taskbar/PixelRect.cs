namespace NetworkSpeed.Core.Taskbar;

/// <summary>Persegi dalam piksel fisik layar (bukan DIP).</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

/// <summary>Koreksi posisi manual dalam piksel (spec T-06).</summary>
public readonly record struct PixelOffset(int X, int Y);
