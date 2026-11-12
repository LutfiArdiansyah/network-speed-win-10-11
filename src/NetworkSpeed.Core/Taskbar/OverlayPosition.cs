namespace NetworkSpeed.Core.Taskbar;

/// <summary>Posisi overlay di taskbar (spec F-07).</summary>
public enum OverlayPosition
{
    Left,
    Center,
    Right,
}

/// <summary>Sisi layar tempat taskbar menempel.</summary>
public enum TaskbarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>Taskbar mana yang menampilkan overlay (spec E-03).</summary>
public enum MonitorMode
{
    PrimaryOnly,
    All,
    Specific,
}
