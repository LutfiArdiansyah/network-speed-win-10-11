namespace NetworkSpeed.Core.Taskbar;

/// <summary>Asal hasil penempatan.</summary>
public enum PlacementSource
{
    Requested,
    FallbackRight,
    Hidden,
}

/// <summary>Koordinat kiri-atas overlay dalam piksel fisik, atau alasan disembunyikan.</summary>
public sealed record Placement(bool IsVisible, int X, int Y, OverlayPosition? Effective, PlacementSource Source, string Reason);

/// <summary>
/// Menghitung posisi overlay (spec F-07, F-07b, T-06, T-10). Overlay tidak pernah ditempatkan di atas tombol app:
/// bila posisi yang diminta tidak didukung atau tidak muat, coba Right; bila tetap tidak muat, sembunyikan.
/// </summary>
public static class PlacementCalculator
{
    public const int Margin = 8;
    public const string NotFoundReason = "Taskbar not found";
    public const string NoSpaceReason = "Not enough space on the taskbar";

    public static Placement Calculate(
        TaskbarLayout? layout,
        OverlayPosition requested,
        int width,
        int height,
        Func<OverlayPosition, PixelOffset> offsetFor)
    {
        ArgumentNullException.ThrowIfNull(offsetFor);

        if (layout is null)
        {
            return Hidden(NotFoundReason);
        }

        if (layout.IsVertical)
        {
            return Hidden(PositionSupportEvaluator.VerticalReason);
        }

        var y = layout.Taskbar.Top + ((layout.Taskbar.Height - height) / 2);
        if (TryPlace(layout, requested, width, out var x))
        {
            return Visible(layout, requested, PlacementSource.Requested, x, y, width, height, offsetFor);
        }

        if (requested != OverlayPosition.Right && TryPlace(layout, OverlayPosition.Right, width, out x))
        {
            return Visible(layout, OverlayPosition.Right, PlacementSource.FallbackRight, x, y, width, height, offsetFor);
        }

        return Hidden(NoSpaceReason);
    }

    private static bool TryPlace(TaskbarLayout l, OverlayPosition position, int width, out int x)
    {
        x = 0;
        if (!PositionSupportEvaluator.IsSupported(l, position))
        {
            return false;
        }

        switch (position)
        {
            case OverlayPosition.Right:
                x = l.TrayLeft - width - Margin;
                return l.LastAppRight is not { } lastRight || x >= lastRight + Margin;

            case OverlayPosition.Center when l.LastAppRight is { } last:
                x = last + Margin;
                return x + width + Margin <= l.TrayLeft;

            case OverlayPosition.Left:
                x = (l.WidgetsRight ?? l.Taskbar.Left) + Margin;
                var limit = l.FirstButtonLeft ?? l.TrayLeft;
                return x + width + Margin <= limit;

            default:
                return false;
        }
    }

    private static Placement Visible(
        TaskbarLayout l, OverlayPosition effective, PlacementSource source, int x, int y, int width, int height,
        Func<OverlayPosition, PixelOffset> offsetFor)
    {
        var offset = offsetFor(effective);

        // Offset adalah koreksi pengguna; tetap dijaga di dalam taskbar.
        var finalX = Math.Clamp(x + offset.X, l.Taskbar.Left, Math.Max(l.Taskbar.Left, l.Taskbar.Right - width));
        var finalY = Math.Clamp(y + offset.Y, l.Taskbar.Top, Math.Max(l.Taskbar.Top, l.Taskbar.Bottom - height));
        var reason = source == PlacementSource.FallbackRight ? $"{effective} used instead of requested position" : string.Empty;
        return new Placement(true, finalX, finalY, effective, source, reason);
    }

    private static Placement Hidden(string reason) => new(false, 0, 0, null, PlacementSource.Hidden, reason);
}
