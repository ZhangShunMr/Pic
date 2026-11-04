using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Avas.Core.Analysis;
using Avas.Core.Domain;

namespace Avas.Desktop.Controls;

public sealed class FieldView : FrameworkElement
{
    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(nameof(Project), typeof(Project), typeof(FieldView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ResultProperty = DependencyProperty.Register(nameof(Result), typeof(Calculation), typeof(FieldView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CoverageOnlyProperty = DependencyProperty.Register(nameof(CoverageOnly), typeof(bool), typeof(FieldView), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SelectedListenerIdProperty = DependencyProperty.Register(nameof(SelectedListenerId), typeof(Guid?), typeof(FieldView), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Project? Project { get => (Project?)GetValue(ProjectProperty); set => SetValue(ProjectProperty, value); }
    public Calculation? Result { get => (Calculation?)GetValue(ResultProperty); set => SetValue(ResultProperty, value); }
    public bool CoverageOnly { get => (bool)GetValue(CoverageOnlyProperty); set => SetValue(CoverageOnlyProperty, value); }
    public Guid? SelectedListenerId { get => (Guid?)GetValue(SelectedListenerIdProperty); set => SetValue(SelectedListenerIdProperty, value); }
    public event EventHandler<Guid>? PointPicked;
    private Rect plot;
    private double scale;
    private readonly Typeface typeface = new("Microsoft YaHei UI");
    private static readonly Brush Ink = Brush("#213637");
    private static readonly Pen GridPen = new(Brush("#B4C6BE"), .5);
    private static readonly Pen Outline = new(Ink, 1.5);

    public FieldView()
    {
        MinWidth = 250;
        MinHeight = 220;
        MouseMove += Hover;
        MouseLeftButtonDown += Pick;
        AutomationPropertiesName();
    }

    private void AutomationPropertiesName() => System.Windows.Automation.AutomationProperties.SetName(this, "车辆周围自由场计算图，X正向为车前，Y正向为车左");
    private static SolidColorBrush Brush(string color)
    {
        var value = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        value.Freeze();
        return value;
    }
    private Point Screen(double x, double y)
    {
        var settings = Project!.Settings;
        return new Point(plot.Left + (x - settings.MinX) * scale, plot.Bottom - (y - settings.MinY) * scale);
    }
    private Point World(Point point)
    {
        var settings = Project!.Settings;
        return new Point(settings.MinX + (point.X - plot.Left) / scale, settings.MinY + (plot.Bottom - point.Y) / scale);
    }
    private void Text(DrawingContext dc, string text, Point at, double size = 12, Brush? color = null)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, color ?? Ink, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, at);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        if (Project is null || ActualWidth < 100 || ActualHeight < 100)
            return;
        var s = Project.Settings;
        scale = Math.Min((ActualWidth - 90) / (s.MaxX - s.MinX), (ActualHeight - 145) / (s.MaxY - s.MinY));
        if (!double.IsFinite(scale) || scale <= 0)
            return;
        var width = (s.MaxX - s.MinX) * scale;
        var height = (s.MaxY - s.MinY) * scale;
        plot = new Rect((ActualWidth - width) / 2 + 10, 62, width, height);
        Text(dc, Result?.Metric ?? "已应用布局 · 无当前有效计算结果", new Point(14, 12), 12);
        dc.DrawRectangle(Brush("#F4F7F1"), Outline, plot);
        dc.PushClip(new RectangleGeometry(plot));
        DrawField(dc);
        DrawGrid(dc);
        DrawObjects(dc);
        dc.Pop();
        DrawAxes(dc);
        DrawLegend(dc);
        if (Result is not null)
            Text(dc, $"有效节点 {Result.Grid.Count} · 工程目标覆盖 {Result.Coverage:F1}%", new Point(14, 33), 10);
    }

    private void DrawField(DrawingContext dc)
    {
        if (Result is null || Project is null)
            return;
        var low = Math.Floor(Result.Minimum / 5) * 5;
        var high = Math.Max(low + 5, Math.Ceiling(Result.Maximum / 5) * 5);
        var palette = Enumerable.Range(0, 128).Select(i => Brush(ChartRenderer.Color(low + (high - low) * i / 127, low, high))).ToArray();
        var yes = Brush("#4E9F76");
        var no = Brush("#E4BD75");
        var half = Project.Settings.Step * scale / 2;
        foreach (var point in Result.Grid)
        {
            var screen = Screen(point.X, point.Y);
            var index = (int)Math.Clamp((point.Signal - low) / (high - low) * 127, 0, 127);
            var color = CoverageOnly ? point.Covered ? yes : no : palette[index];
            dc.DrawRectangle(color, null, new Rect(screen.X - half, screen.Y - half, half * 2 + .5, half * 2 + .5));
        }
    }

    private void DrawGrid(DrawingContext dc)
    {
        var s = Project!.Settings;
        var tick = TickSize(Math.Max(s.MaxX - s.MinX, s.MaxY - s.MinY));
        for (var x = Math.Ceiling(s.MinX / tick) * tick; x <= s.MaxX; x += tick)
            dc.DrawLine(GridPen, Screen(x, s.MinY), Screen(x, s.MaxY));
        for (var y = Math.Ceiling(s.MinY / tick) * tick; y <= s.MaxY; y += tick)
            dc.DrawLine(GridPen, Screen(s.MinX, y), Screen(s.MaxX, y));
    }
    private static double TickSize(double extent)
    {
        var raw = extent / 7;
        var exponent = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        return Math.Ceiling(raw / exponent) * exponent;
    }
    private void DrawObjects(DrawingContext dc)
    {
        var project = Project!;
        var s = project.Settings;
        var corner = Screen(-s.VehicleLength / 2, s.VehicleWidth / 2);
        var vehicle = new Rect(corner, new Size(s.VehicleLength * scale, s.VehicleWidth * scale));
        dc.DrawRoundedRectangle(Brush("#E8EBE7"), Outline, vehicle, 9, 9);
        Text(dc, "车前 →", new Point(vehicle.Left + 8, vehicle.Top + Math.Max(0, vehicle.Height / 2 - 8)), 11);
        foreach (var speaker in project.Speakers)
        {
            var point = Screen(speaker.X, speaker.Y);
            var angle = speaker.Heading * Math.PI / 180;
            var end = new Point(point.X + 28 * Math.Cos(angle), point.Y - 28 * Math.Sin(angle));
            dc.DrawLine(new Pen(Ink, 3), point, end);
            dc.DrawEllipse(speaker.Enabled ? Brushes.White : Brushes.Gray, Outline, point, 6, 6);
        }
        foreach (var listener in project.Listeners)
        {
            var point = Screen(listener.X, listener.Y);
            var selected = listener.Id == SelectedListenerId;
            dc.DrawEllipse(selected ? Brush("#F2DA58") : Brushes.White, new Pen(Ink, selected ? 3 : 1.5), point, selected ? 7 : 4, selected ? 7 : 4);
            Text(dc, listener.Name, new Point(point.X + 9, point.Y - 20), 11);
        }
    }
    private void DrawAxes(DrawingContext dc)
    {
        var s = Project!.Settings;
        var tick = TickSize(Math.Max(s.MaxX - s.MinX, s.MaxY - s.MinY));
        for (var x = Math.Ceiling(s.MinX / tick) * tick; x <= s.MaxX; x += tick)
            Text(dc, x.ToString("0.#", CultureInfo.InvariantCulture), new Point(Screen(x, s.MinY).X - 7, plot.Bottom + 6), 10);
        for (var y = Math.Ceiling(s.MinY / tick) * tick; y <= s.MaxY; y += tick)
            Text(dc, y.ToString("0.#", CultureInfo.InvariantCulture), new Point(plot.Left - 34, Screen(s.MinX, y).Y - 7), 10);
        Text(dc, "X / m → 车前     Y / m ↑ 车左", new Point(plot.Left, plot.Bottom + 25), 11);
        Text(dc, "轮廓仅示意，不模拟遮挡；网格节点覆盖不代表人群可听率。", new Point(14, ActualHeight - 22), 10);
    }
    private void DrawLegend(DrawingContext dc)
    {
        if (Result is null)
            return;
        var y = plot.Bottom + 45;
        if (CoverageOnly)
        {
            dc.DrawRectangle(Brush("#4E9F76"), null, new Rect(14, y, 14, 12));
            Text(dc, "满足工程目标", new Point(34, y - 2), 10);
            dc.DrawRectangle(Brush("#E4BD75"), null, new Rect(155, y, 14, 12));
            Text(dc, "未满足", new Point(175, y - 2), 10);
        }
        else
        {
            var low = Math.Floor(Result.Minimum / 5) * 5;
            var high = Math.Max(low + 5, Math.Ceiling(Result.Maximum / 5) * 5);
            for (var i = 0; i < 100; i++)
                dc.DrawRectangle(Brush(ChartRenderer.Color(low + (high - low) * i / 99, low, high)), null, new Rect(14 + i, y, 1.5, 10));
            Text(dc, $"{low:F0}—{high:F0} dB", new Point(124, y - 2), 10);
        }
    }
    private void Pick(object sender, MouseButtonEventArgs e)
    {
        if (Project is null || scale <= 0)
            return;
        var mouse = e.GetPosition(this);
        var nearest = Project.Listeners.OrderBy(p => (Screen(p.X, p.Y) - mouse).LengthSquared).FirstOrDefault();
        if (nearest is not null && (Screen(nearest.X, nearest.Y) - mouse).Length <= 16)
            PointPicked?.Invoke(this, nearest.Id);
    }
    private void Hover(object sender, MouseEventArgs e)
    {
        if (Project is null || scale <= 0 || !plot.Contains(e.GetPosition(this)))
        {
            ToolTip = null;
            return;
        }
        var p = World(e.GetPosition(this));
        var result = Result?.Grid.OrderBy(q => Math.Pow(q.X - p.X, 2) + Math.Pow(q.Y - p.Y, 2)).FirstOrDefault();
        ToolTip = result is null ? $"X={p.X:F2} m，Y={p.Y:F2} m；当前无有效结果"
            : $"最近有效节点 ({result.X:F2}, {result.Y:F2}) m\n警示声 {result.Signal:F2} dB，背景差 {result.Margin:F2} dB\n{(result.Covered ? "满足工程目标" : "未满足工程目标")}；距离截断 {result.NearFieldClamped}";
    }
}
