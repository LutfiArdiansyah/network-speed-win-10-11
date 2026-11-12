using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using NetworkSpeed.Core.Display;
using NetworkSpeed.Core.Formatting;

namespace NetworkSpeed.App.History;

/// <summary>
/// Grafik garis ↑/↓ sederhana (spec H-02) yang digambar langsung di <see cref="OnRender"/>, tanpa paket chart.
/// Nilai dalam byte per detik; sumbu Y memakai <see cref="ChartScale.NiceMax"/>.
/// </summary>
public sealed class TrafficChart : FrameworkElement
{
    private const double LabelMargin = 4;
    private IReadOnlyList<(double Up, double Down)> _values = [];

    public SpeedUnitMode UnitMode { get; set; }

    public Brush UpBrush { get; set; } = Brushes.DarkOrange;

    public Brush DownBrush { get; set; } = Brushes.DodgerBlue;

    public void SetValues(IReadOnlyList<(double Up, double Down)> values)
    {
        _values = values;
        var peakDown = values.Count == 0 ? 0 : values.Max(v => v.Down);
        var peakUp = values.Count == 0 ? 0 : values.Max(v => v.Up);
        AutomationProperties.SetName(this, $"Traffic chart. Download peak {SpeedFormatter.Format(peakDown, UnitMode)}, upload peak {SpeedFormatter.Format(peakUp, UnitMode)}.");
        InvalidateVisual();
    }

    // FrameworkElement tidak punya automation peer bawaan; tanpa ini grafik tidak terlihat oleh screen reader (spec D-06).
    protected override AutomationPeer OnCreateAutomationPeer() => new ChartAutomationPeer(this);

    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var dc = drawingContext;
        var width = ActualWidth;
        var height = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var axis = new Pen(SystemColors.GrayTextBrush, 1);
        axis.Freeze();
        dc.DrawLine(axis, new Point(0, height - 0.5), new Point(width, height - 0.5));
        if (_values.Count < 2)
        {
            return;
        }

        var max = ChartScale.NiceMax(_values.Max(v => Math.Max(v.Up, v.Down)));
        dc.DrawLine(axis, new Point(0, 0.5), new Point(width, 0.5));
        var label = new FormattedText(
            SpeedFormatter.Format(max, UnitMode),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            11,
            SystemColors.GrayTextBrush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(label, new Point(LabelMargin, LabelMargin));

        DrawLine(dc, _values.Select(v => v.Down).ToList(), max, width, height, DownBrush);
        DrawLine(dc, _values.Select(v => v.Up).ToList(), max, width, height, UpBrush);
    }

    private sealed class ChartAutomationPeer(TrafficChart owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

        protected override string GetClassNameCore() => nameof(TrafficChart);
    }

    private static void DrawLine(DrawingContext dc, List<double> values, double max, double width, double height, Brush brush)
    {
        var step = width / (values.Count - 1);
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var i = 0; i < values.Count; i++)
            {
                var y = height - (Math.Clamp(values[i], 0, max) / max * (height - 1));
                var point = new Point(i * step, y);
                if (i == 0)
                {
                    ctx.BeginFigure(point, false, false);
                }
                else
                {
                    ctx.LineTo(point, true, false);
                }
            }
        }

        geometry.Freeze();
        var pen = new Pen(brush, 1.5);
        pen.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }
}
