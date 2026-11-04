using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Cabin.Desktop.ViewModels;

namespace Cabin.Desktop.Controls;

public sealed class SpectrumChart : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(PlotData), typeof(SpectrumChart),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedXProperty = DependencyProperty.Register(
        nameof(SelectedX), typeof(double), typeof(SpectrumChart),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender |
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    private Rect plot;
    private double minimumX;
    private double maximumX;
    private double minimumY;
    private double maximumY;

    public SpectrumChart()
    {
        Focusable = true;
        MinHeight = 260;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
        ToolTip = "点击曲线横坐标可选择研究频率；纵坐标为相对量。";
    }

    public PlotData? Data
    {
        get => (PlotData?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public double SelectedX
    {
        get => (double)GetValue(SelectedXProperty);
        set => SetValue(SelectedXProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        context.DrawRectangle(DrawingTools.Brush("#FFFFFF"), null, new Rect(0, 0, ActualWidth, ActualHeight));
        var data = Data;
        var points = data?.Lines.SelectMany(x => x.Points).Where(x => double.IsFinite(x.X) && double.IsFinite(x.Y)).ToArray();
        if (data is null || points is null || points.Length == 0)
        {
            DrawingTools.Text(context, "暂无曲线。请完成输入并计算。", 22, 30);
            return;
        }
        var legendRows = Math.Max(1, (int)Math.Ceiling(data.Lines.Count / 3.0));
        plot = new Rect(76, 58, Math.Max(1, ActualWidth - 104), Math.Max(1, ActualHeight - 120 - legendRows * 23));
        minimumX = points.Min(x => x.X);
        maximumX = points.Max(x => x.X);
        minimumY = points.Min(x => x.Y);
        maximumY = points.Max(x => x.Y);
        if (minimumX == maximumX)
        {
            minimumX -= 0.5;
            maximumX += 0.5;
        }
        if (minimumY == maximumY)
        {
            minimumY -= Math.Max(0.1, Math.Abs(minimumY) * 0.1);
            maximumY += Math.Max(0.1, Math.Abs(maximumY) * 0.1);
        }
        var padding = (maximumY - minimumY) * 0.05;
        minimumY -= padding;
        maximumY += padding;
        DrawingTools.Text(context, data.Title, 18, 10, 14, bold: true, maximumWidth: ActualWidth - 30);
        DrawingTools.Text(context, data.YLabel, 12, 36, 11);
        DrawGrid(context);
        context.PushClip(new RectangleGeometry(plot));
        foreach (var line in data.Lines)
            DrawSeries(context, line);
        if (double.IsFinite(SelectedX) && SelectedX >= minimumX && SelectedX <= maximumX)
        {
            var x = MapX(SelectedX);
            DrawingTools.Line(context, x, plot.Top, x, plot.Bottom, "#C35D33", 1.5);
        }
        context.Pop();
        DrawingTools.Text(context, data.XLabel, plot.Left + plot.Width / 2 - 40, plot.Bottom + 31, 11);
        for (var i = 0; i < data.Lines.Count; i++)
        {
            var x = plot.Left + i % 3 * plot.Width / 3;
            var y = plot.Bottom + 57 + i / 3 * 23;
            DrawingTools.Line(context, x, y + 7, x + 20, y + 7, data.Lines[i].Color, 2);
            DrawingTools.Text(context, data.Lines[i].Name, x + 26, y, 11, maximumWidth: plot.Width / 3 - 32);
        }
    }

    private void DrawGrid(DrawingContext context)
    {
        for (var i = 0; i <= 5; i++)
        {
            var x = plot.Left + plot.Width * i / 5;
            var y = plot.Bottom - plot.Height * i / 5;
            DrawingTools.Line(context, x, plot.Top, x, plot.Bottom, "#E4EBEC");
            DrawingTools.Line(context, plot.Left, y, plot.Right, y, "#E4EBEC");
            DrawingTools.Text(context, DrawingTools.Number(minimumX + (maximumX - minimumX) * i / 5), x - 13, plot.Bottom + 7, 10);
            DrawingTools.Text(context, DrawingTools.Number(minimumY + (maximumY - minimumY) * i / 5), 10, y - 7, 10, maximumWidth: 62);
        }
        context.DrawRectangle(null, new Pen(DrawingTools.Brush("#8EA8AE"), 1), plot);
    }

    private void DrawSeries(DrawingContext context, PlotLine line)
    {
        if (line.Points.Count == 0)
            return;
        var geometry = new StreamGeometry();
        using (var drawing = geometry.Open())
        {
            var started = false;
            foreach (var point in line.Points)
            {
                if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
                {
                    started = false;
                    continue;
                }
                var mapped = new Point(MapX(point.X), MapY(point.Y));
                if (!started)
                {
                    drawing.BeginFigure(mapped, false, false);
                    started = true;
                }
                else
                    drawing.LineTo(mapped, true, false);
            }
        }
        geometry.Freeze();
        context.DrawGeometry(null, new Pen(DrawingTools.Brush(line.Color), 1.8), geometry);
        if (line.Points.Count <= 25)
        {
            foreach (var point in line.Points.Where(p => double.IsFinite(p.X) && double.IsFinite(p.Y)))
                context.DrawEllipse(DrawingTools.Brush(line.Color), null, new Point(MapX(point.X), MapY(point.Y)), 3, 3);
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var position = e.GetPosition(this);
        if (Data is null || !plot.Contains(position))
            return;
        SetCurrentValue(SelectedXProperty, minimumX + (position.X - plot.Left) / plot.Width * (maximumX - minimumX));
        Focus();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);
        if (Data is null || !plot.Contains(position))
            return;
        var x = minimumX + (position.X - plot.Left) / plot.Width * (maximumX - minimumX);
        var values = Data.Lines.Where(l => l.Points.Count > 0).Select(l =>
        {
            var point = l.Points.MinBy(p => Math.Abs(p.X - x))!;
            return l.Name + ": " + DrawingTools.Number(point.Y);
        });
        ToolTip = DrawingTools.Number(x) + " | 最近样点\n" + string.Join("\n", values);
    }

    private double MapX(double value) => plot.Left + (value - minimumX) / (maximumX - minimumX) * plot.Width;
    private double MapY(double value) => plot.Bottom - (value - minimumY) / (maximumY - minimumY) * plot.Height;
}
