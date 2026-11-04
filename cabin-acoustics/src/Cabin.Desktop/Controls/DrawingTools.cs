using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Cabin.Desktop.Controls;

internal static class DrawingTools
{
    public static Brush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    public static void Text(DrawingContext context, string text, double x, double y,
        double size = 12, string color = "#395762", bool bold = false, double maximumWidth = 10000)
    {
        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Microsoft YaHei UI, Segoe UI"), FontStyles.Normal,
                bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
            size, Brush(color), 1.0)
        {
            MaxTextWidth = Math.Max(1, maximumWidth),
            Trimming = TextTrimming.CharacterEllipsis,
            MaxLineCount = 2
        };
        context.DrawText(formatted, new Point(x, y));
    }

    public static string Number(double value)
    {
        if (Math.Abs(value) >= 10000 || (Math.Abs(value) < 0.001 && value != 0))
            return value.ToString("0.##E+0", CultureInfo.InvariantCulture);
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public static void Line(DrawingContext context, double x1, double y1, double x2, double y2,
        string color, double width = 1)
    {
        context.DrawLine(new Pen(Brush(color), width), new Point(x1, y1), new Point(x2, y2));
    }
}
