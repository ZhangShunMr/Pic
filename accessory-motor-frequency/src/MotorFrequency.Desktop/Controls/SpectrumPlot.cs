using System.Windows;
using System.Windows.Media;
using MotorFrequency.Core.Domain;
using MotorFrequency.Core.Services;

namespace MotorFrequency.Desktop.Controls;

public sealed class SpectrumPlot : PlotBase
{
    public static readonly DependencyProperty SpectrumProperty = DependencyProperty.Register(
        nameof(Spectrum), typeof(Spectrum), typeof(SpectrumPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty MaximumHzProperty = DependencyProperty.Register(
        nameof(MaximumHz), typeof(double), typeof(SpectrumPlot), new FrameworkPropertyMetadata(1500.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project), typeof(Project), typeof(SpectrumPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public Spectrum? Spectrum { get => (Spectrum?)GetValue(SpectrumProperty); set => SetValue(SpectrumProperty, value); }
    public double MaximumHz { get => (double)GetValue(MaximumHzProperty); set => SetValue(MaximumHzProperty, value); }
    public Project? Project { get => (Project?)GetValue(ProjectProperty); set => SetValue(ProjectProperty, value); }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (Spectrum is null || Spectrum.Points.Count < 2 || MaximumHz <= 0)
        {
            Empty(drawing, "选择或导入一条频谱后显示原始幅值曲线。");
            return;
        }
        var shown = Spectrum.Points.Where(x => x.FrequencyHz <= MaximumHz).ToArray();
        if (shown.Length == 0)
        {
            Empty(drawing, "原始频点均在当前图表范围外，请调整图表频率上限。");
            return;
        }
        var low = shown.Min(x => x.Amplitude);
        var high = shown.Max(x => x.Amplitude);
        var padding = Math.Max(1, (high - low) * 0.08);
        low -= padding;
        high += padding;
        Axes(drawing, 0, MaximumHz, low, high, "频率 Hz", "幅值 / " + Spectrum.Unit);
        var area = PlotArea;
        Point Map(SpectrumPoint point) => new(area.Left + point.FrequencyHz / MaximumHz * area.Width,
            area.Bottom - (point.Amplitude - low) / (high - low) * area.Height);
        var points = ChartGeometry.DisplayPoints(Spectrum).Where(x => x.FrequencyHz <= MaximumHz).ToArray();
        drawing.PushClip(new RectangleGeometry(area));
        if (points.Length >= 2)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(Map(points[0]), false, false);
                for (var index = 1; index < points.Length; index++)
                {
                    context.LineTo(Map(points[index]), true, false);
                }
            }
            geometry.Freeze();
            drawing.DrawGeometry(null, new Pen(ColorBrush("#12766F"), 1.5), geometry);
        }
        if (Project is not null)
        {
            foreach (var frequency in FrequencyEngine.MatchPeaks(Project, Spectrum).Select(x => x.ObservedHz).Distinct())
            {
                var original = Spectrum.Points.First(x => x.FrequencyHz == frequency);
                drawing.DrawEllipse(ColorBrush("#BB742E"), new Pen(Brushes.White, 1), Map(original), 4, 4);
            }
        }
        drawing.Pop();
        Text(drawing, Labels.Origin(Spectrum.Origin) + " · " + Spectrum.Rpm.ToString("G6") + " r/min · 仅选定频谱", new Point(area.Left, 7), Muted);
    }
}
