using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Blower.Core.Analysis;

namespace Blower.Desktop.Controls;

public sealed class FrequencyCanvas : FrameworkElement
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(
        nameof(Model), typeof(ChartModel), typeof(FrequencyCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SourceLabelProperty = DependencyProperty.Register(
        nameof(SourceLabel), typeof(string), typeof(FrequencyCanvas),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public ChartModel? Model
    {
        get => (ChartModel?)GetValue(ModelProperty);
        set => SetValue(ModelProperty, value);
    }
    public string SourceLabel
    {
        get => (string)GetValue(SourceLabelProperty);
        set => SetValue(SourceLabelProperty, value);
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        var chart = Model;
        if (chart == null)
        {
            DrawText(dc, "尚无有效结果。填写输入后按 F5 计算。", 30, 40, 15, Brushes.SlateGray);
            return;
        }
        if (ActualWidth < 150 || ActualHeight < 150)
        {
            return;
        }
        var sx = ActualWidth / ChartModel.Width;
        var sy = ActualHeight / (ChartModel.Height + 60);
        double X(double x) => x * sx;
        double Y(double y) => y * sy;
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(220, 230, 230)), 1);
        foreach (var tick in chart.XTicks)
        {
            dc.DrawLine(gridPen, new(X(tick.Position), Y(ChartModel.Top)), new(X(tick.Position), Y(ChartModel.Bottom)));
            DrawText(dc, tick.Label, X(tick.Position) - 16, Y(ChartModel.Bottom) + 8, 11, Brushes.DimGray);
        }
        foreach (var tick in chart.YTicks)
        {
            dc.DrawLine(gridPen, new(X(ChartModel.Left), Y(tick.Position)), new(X(ChartModel.Right), Y(tick.Position)));
            DrawText(dc, tick.Label, 5, Y(tick.Position) - 8, 11, Brushes.DimGray);
        }
        foreach (var band in chart.Bands)
        {
            var brush = new SolidColorBrush(Color.FromArgb(55, 125, 100, 174));
            dc.DrawRectangle(brush, null, new Rect(X(ChartModel.Left), Y(band.Y),
                X(ChartModel.Right - ChartModel.Left), Math.Max(2, Y(band.Height))));
            DrawText(dc, band.Name, X(ChartModel.Left) + 6, Y(band.LabelY) - 12, 11, Brushes.DarkSlateBlue);
        }
        foreach (var line in chart.Lines)
        {
            var brush = (Brush)new BrushConverter().ConvertFromString(line.Color)!;
            dc.DrawLine(new Pen(brush, 2), new(X(line.X1), Y(line.Y1)), new(X(line.X2), Y(line.Y2)));
        }
        foreach (var gear in chart.Gears)
        {
            var brush = gear.Near ? Brushes.Firebrick : Brushes.Teal;
            var pen = new Pen(brush, 1) { DashStyle = DashStyles.Dash };
            dc.DrawLine(pen, new(X(gear.X), Y(ChartModel.Top)), new(X(gear.X), Y(ChartModel.Bottom)));
            DrawText(dc, gear.Name, X(gear.X) - 15, 5, 11, brush);
        }
        DrawText(dc, "频率 Hz", 5, 5, 12, Brushes.DarkSlateGray);
        DrawText(dc, "转速 rpm →", X(ChartModel.Left), Y(390), 12, Brushes.DarkSlateGray);
        DrawText(dc, SourceLabel + " · 紫色：模态区间；红虚线：存在接近；绿虚线：未接近", X(ChartModel.Left), Y(413), 11, Brushes.DarkSlateGray);
        var legendX = X(ChartModel.Left);
        var legendY = Y(438);
        foreach (var line in chart.Lines)
        {
            var brush = (Brush)new BrushConverter().ConvertFromString(line.Color)!;
            var text = DrawText(dc, line.Name, legendX, legendY, 11, brush);
            legendX += text.Width + 22;
            if (legendX > ActualWidth - 170)
            {
                legendX = X(ChartModel.Left);
                legendY += 16;
            }
        }
    }
    private FormattedText DrawText(DrawingContext dc, string text, double x, double y, double size, Brush brush)
    {
        var formatted = new FormattedText(text, CultureInfo.GetCultureInfo("zh-CN"), FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, new Point(x, y));
        return formatted;
    }
}
