using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace MotorFrequency.Desktop.Controls;

public abstract class PlotBase : FrameworkElement
{
    protected Rect PlotArea => new(62, 28, Math.Max(1, ActualWidth - 85), Math.Max(1, ActualHeight - 84));
    protected static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(45, 67, 80));
    protected static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(83, 106, 117));
    protected static readonly Pen GridPen = new(new SolidColorBrush(Color.FromRgb(220, 230, 234)), 1);
    protected static readonly Pen BorderPen = new(new SolidColorBrush(Color.FromRgb(142, 165, 176)), 1);

    protected PlotBase()
    {
        ClipToBounds = true;
        SnapsToDevicePixels = true;
        SizeChanged += (_, _) => InvalidateVisual();
    }

    protected void Text(DrawingContext drawing, string value, Point at, Brush? color = null,
        double size = 11, bool right = false)
    {
        var formatted = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), size, color ?? Ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        if (right)
        {
            at.X -= formatted.Width;
        }
        drawing.DrawText(formatted, at);
    }

    protected void Empty(DrawingContext drawing, string message)
    {
        drawing.DrawRectangle(Brushes.WhiteSmoke, null, new Rect(RenderSize));
        Text(drawing, message, new Point(24, Math.Max(24, ActualHeight / 2)), Muted, 13);
    }

    protected void Axes(DrawingContext drawing, double xMinimum, double xMaximum,
        double yMinimum, double yMaximum, string xUnit, string yUnit)
    {
        var area = PlotArea;
        drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(249, 252, 252)), null, new Rect(RenderSize));
        for (var index = 0; index <= 5; index++)
        {
            var fraction = index / 5.0;
            var x = area.Left + area.Width * fraction;
            var y = area.Bottom - area.Height * fraction;
            drawing.DrawLine(GridPen, new Point(x, area.Top), new Point(x, area.Bottom));
            drawing.DrawLine(GridPen, new Point(area.Left, y), new Point(area.Right, y));
            Text(drawing, (xMinimum + (xMaximum - xMinimum) * fraction).ToString("G5", CultureInfo.InvariantCulture),
                new Point(x - 12, area.Bottom + 8));
            Text(drawing, (yMinimum + (yMaximum - yMinimum) * fraction).ToString("G5", CultureInfo.InvariantCulture),
                new Point(area.Left - 8, y - 7), right: true);
        }
        drawing.DrawRectangle(null, BorderPen, area);
        Text(drawing, yUnit, new Point(6, 5), Muted);
        Text(drawing, xUnit, new Point(area.Right, ActualHeight - 22), Muted, right: true);
    }

    protected static Brush ColorBrush(string value)
    {
        return new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
    }
}
