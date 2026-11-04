using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Cabin.Core.Analysis;
using Cabin.Core.Domain;

namespace Cabin.Desktop.Controls;

public sealed class FieldCanvas : FrameworkElement
{
    public static readonly DependencyProperty FieldProperty = DependencyProperty.Register(
        nameof(Field), typeof(FieldResult), typeof(FieldCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project), typeof(Project), typeof(FieldCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    private Rect region;
    private int columns;
    private int rows;

    public FieldCanvas()
    {
        MinHeight = 330;
        ClipToBounds = true;
        ToolTip = "移动指针读取最近网格点的坐标和相对量。";
    }

    public FieldResult? Field
    {
        get => (FieldResult?)GetValue(FieldProperty);
        set => SetValue(FieldProperty, value);
    }

    public Project? Project
    {
        get => (Project?)GetValue(ProjectProperty);
        set => SetValue(ProjectProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        context.DrawRectangle(DrawingTools.Brush("#FFFFFF"), null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (Field is null || Project is null || Field.Cells.Count == 0)
        {
            DrawingTools.Text(context, "剖面待更新", 24, 28, 18, bold: true);
            DrawingTools.Text(context, "完成计算后，选择剖面、频率及显示量，再点击“更新剖面与贡献”。", 24, 66, maximumWidth: ActualWidth - 48);
            return;
        }
        var f = Field;
        var model = Project.Model;
        var horizontal = f.Plane == "yz" ? model.Width : model.Length;
        var vertical = f.Plane == "xy" ? model.Width : model.Height;
        var availableWidth = Math.Max(1, ActualWidth - 110);
        var availableHeight = Math.Max(1, ActualHeight - 155);
        var scale = Math.Min(availableWidth / horizontal, availableHeight / vertical);
        var width = horizontal * scale;
        var height = vertical * scale;
        region = new Rect(65 + (availableWidth - width) / 2, 58 + (availableHeight - height) / 2, width, height);
        columns = f.Cells.Select(x => x.U).Distinct().Count();
        rows = f.Cells.Select(x => x.V).Distinct().Count();
        DrawingTools.Text(context, f.Quantity, 18, 12, 15, bold: true, maximumWidth: ActualWidth - 36);
        DrawingTools.Text(context, (Project.IsDemo ? "模拟数据 · " : "人工理想模型 · ") + f.Plane.ToUpperInvariant() + " 固定轴比例=" + DrawingTools.Number(f.Slice), 18, 36, 11);
        var shape = f.Quantity.Contains("振型", StringComparison.Ordinal);
        foreach (var cell in f.Cells)
        {
            var rectangle = new Rect(region.Left + (cell.U - 0.5 / columns) * region.Width,
                region.Bottom - (cell.V + 0.5 / rows) * region.Height, region.Width / columns + 0.5, region.Height / rows + 0.5);
            context.DrawRectangle(ColorBrush(cell.Value, f.Maximum, shape), null, rectangle);
        }
        context.DrawRectangle(null, new Pen(DrawingTools.Brush("#3A5D6A"), 1.2), region);
        for (var i = 0; i <= 4; i++)
        {
            var x = region.Left + region.Width * i / 4;
            var y = region.Bottom - region.Height * i / 4;
            DrawingTools.Line(context, x, region.Bottom, x, region.Bottom + 5, "#3A5D6A");
            DrawingTools.Line(context, region.Left - 5, y, region.Left, y, "#3A5D6A");
            DrawingTools.Text(context, DrawingTools.Number(horizontal * i / 4), x - 10, region.Bottom + 9, 10);
            DrawingTools.Text(context, DrawingTools.Number(vertical * i / 4), region.Left - 44, y - 7, 10);
        }
        DrawingTools.Text(context, (f.Plane == "yz" ? "y" : "x") + " / m", region.Right + 8, region.Bottom + 9, 11);
        DrawingTools.Text(context, (f.Plane == "xy" ? "y" : "z") + " / m", region.Left - 42, region.Top - 25, 11);
        DrawMarkers(context, model, f);
        DrawLegend(context, f, shape);
    }

    private void DrawMarkers(DrawingContext context, Cavity model, FieldResult field)
    {
        foreach (var source in Project!.Sources)
        {
            var uv = Engine.Project(source.Point, model, field.Plane);
            var point = Map(uv.U, uv.V);
            var brush = DrawingTools.Brush(source.Enabled ? "#C85C2D" : "#7B8588");
            context.DrawRectangle(brush, new Pen(Brushes.White, 1.2), new Rect(point.X - 5, point.Y - 5, 10, 10));
            DrawingTools.Text(context, source.Name, point.X + 7, point.Y - 16, 10, "#38291F", maximumWidth: 120);
        }
        foreach (var seat in Project.Seats)
        {
            var uv = Engine.Project(seat.Point, model, field.Plane);
            var point = Map(uv.U, uv.V);
            context.DrawEllipse(Brushes.White, new Pen(DrawingTools.Brush("#204659"), 2), point, 5, 5);
            DrawingTools.Text(context, seat.Name, point.X + 7, point.Y + 3, 10, "#183648", maximumWidth: 120);
        }
    }

    private void DrawLegend(DrawingContext context, FieldResult field, bool shape)
    {
        var y = ActualHeight - 65;
        var legendWidth = Math.Min(210, ActualWidth - 150);
        for (var i = 0; i < 100; i++)
        {
            var value = shape ? i / 99.0 * 2 - 1 : i / 99.0;
            context.DrawRectangle(ColorBrush(value, 1, shape), null, new Rect(65 + i / 100.0 * legendWidth, y, legendWidth / 100 + 1, 10));
        }
        DrawingTools.Text(context, shape ? "−1" : "0", 65, y + 12, 10);
        DrawingTools.Text(context, "1", 65 + legendWidth - 5, y + 12, 10);
        DrawingTools.Text(context, "剖面最大值 " + DrawingTools.Number(field.Maximum) + (shape ? "（振型）" : "（无量纲）"), 65 + legendWidth + 20, y - 3, 10, maximumWidth: ActualWidth - legendWidth - 100);
        DrawingTools.Text(context, "□ 声源   ○ 耳点（均为投影） · 非实车绝对声压", 20, ActualHeight - 22, 10);
    }

    private static Brush ColorBrush(double value, double maximum, bool shape)
    {
        var ratio = maximum > 1e-15 ? Math.Clamp(value / maximum, shape ? -1 : 0, 1) : 0;
        var start = Color.FromRgb(247, 246, 236);
        var end = shape && ratio < 0 ? Color.FromRgb(36, 110, 146) : Color.FromRgb(193, 92, 42);
        var amount = Math.Abs(ratio);
        var color = Color.FromRgb((byte)(start.R + (end.R - start.R) * amount),
            (byte)(start.G + (end.G - start.G) * amount), (byte)(start.B + (end.B - start.B) * amount));
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);
        if (Field is null || Project is null || !region.Contains(position))
            return;
        var u = (position.X - region.Left) / region.Width;
        var v = (region.Bottom - position.Y) / region.Height;
        var cell = Field.Cells.MinBy(x => Math.Pow(x.U - u, 2) + Math.Pow(x.V - v, 2))!;
        var point = Engine.PlanePoint(Project.Model, Field.Plane, cell.U, cell.V, Field.Slice);
        ToolTip = $"网格点 x={point.X:F3}m, y={point.Y:F3}m, z={point.Z:F3}m\n{Field.Quantity}：{DrawingTools.Number(cell.Value)}";
    }

    private Point Map(double u, double v)
    {
        return new Point(region.Left + u * region.Width, region.Bottom - v * region.Height);
    }
}
