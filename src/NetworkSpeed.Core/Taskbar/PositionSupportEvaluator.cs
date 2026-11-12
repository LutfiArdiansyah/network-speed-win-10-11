namespace NetworkSpeed.Core.Taskbar;

/// <summary>Status dukungan satu posisi pada satu taskbar (spec F-07a).</summary>
public sealed record PositionSupport(OverlayPosition Position, bool IsSupported, string? Reason);

/// <summary>Matriks dukungan posisi hasil spike (spec F-07c).</summary>
public static class PositionSupportEvaluator
{
    public const string VerticalReason = "Vertical taskbar is not supported";
    public const string LeftCornerReason = "Left corner is used by Start on this taskbar";
    public const string NoDetailReason = "Taskbar details are unavailable";

    public static IReadOnlyList<PositionSupport> Evaluate(TaskbarLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);

        if (layout.IsVertical)
        {
            return
            [
                new(OverlayPosition.Left, false, VerticalReason),
                new(OverlayPosition.Center, false, VerticalReason),
                new(OverlayPosition.Right, false, VerticalReason),
            ];
        }

        return
        [
            new(OverlayPosition.Left, IsLeftSupported(layout), LeftReason(layout)),
            new(OverlayPosition.Center, layout.HasUiaDetail, layout.HasUiaDetail ? null : NoDetailReason),
            new(OverlayPosition.Right, true, null),
        ];
    }

    public static bool IsSupported(TaskbarLayout layout, OverlayPosition position) =>
        Evaluate(layout).Single(s => s.Position == position).IsSupported;

    private static bool IsLeftSupported(TaskbarLayout l) => l.IsWindows11 && l.IsCentered && l.HasUiaDetail;

    private static string? LeftReason(TaskbarLayout l) => IsLeftSupported(l)
        ? null
        : l.HasUiaDetail ? LeftCornerReason : NoDetailReason;
}
