using System.Collections;
using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using AcousticSeal.Core.Analysis;

namespace AcousticSeal.Desktop;

public sealed class SpectrumPlot : FrameworkElement
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(nameof(Items), typeof(IEnumerable), typeof(SpectrumPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, ItemsChanged));
    public static readonly DependencyProperty OriginalProperty = DependencyProperty.Register(nameof(Original), typeof(bool), typeof(SpectrumPlot), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public IEnumerable? Items { get => (IEnumerable?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public bool Original { get => (bool)GetValue(OriginalProperty); set => SetValue(OriginalProperty, value); }
    private static void ItemsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var plot = (SpectrumPlot)sender;
        if (args.OldValue is INotifyCollectionChanged oldItems) oldItems.CollectionChanged -= plot.CollectionChanged;
        if (args.NewValue is INotifyCollectionChanged newItems) newItems.CollectionChanged += plot.CollectionChanged;
    }
    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var bands = Items?.Cast<BandDifference>().OrderBy(item => item.Frequency).ToList() ?? [];
        var left = 66.0; var top = 38.0;
        var width = Math.Max(10, ActualWidth - left - 30);
        var height = Math.Max(10, ActualHeight - top - 58);
        var teal = new SolidColorBrush(Color.FromRgb(30, 105, 103));
        var copper = new SolidColorBrush(Color.FromRgb(182, 93, 51));
        void Text(string text, double x, double y, Brush? brush = null, double size = 12)
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Microsoft YaHei UI"), size, brush ?? Brushes.DimGray, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(formatted, new Point(x, y));
        }
        Text(Original ? "基线（青） / 目标（铜） · dB re 20 µPa · 纵轴截断" : "测得声级差 ΔL = 基线 − 目标 (dB) · 零基准", 10, 8, teal);
        if (bands.Count == 0 || bands.All(item => Original ? !item.Baseline.HasValue && !item.Target.HasValue : !item.Difference.HasValue))
        {
            Text("无可绘制数据；检查配对状态、缺失频带或显示范围。", left, top + 50);
            return;
        }
        var values = bands.SelectMany(item => Original ? new[] { item.Baseline, item.Target } : new[] { item.Difference }).Where(value => value.HasValue).Select(value => value!.Value).ToList();
        var min = Original ? Math.Floor(values.Min() - 1) : Math.Min(0, Math.Floor(values.Min() - 0.5));
        var max = Original ? Math.Ceiling(values.Max() + 1) : Math.Max(0, Math.Ceiling(values.Max() + 0.5));
        if (max <= min) max = min + 1;
        var xMin = Math.Log10(bands.Min(item => item.Frequency));
        var xMax = Math.Log10(bands.Max(item => item.Frequency));
        if (xMax <= xMin) { xMin -= 0.2; xMax += 0.2; }
        Point Position(double frequency, double value) => new(left + (Math.Log10(frequency) - xMin) / (xMax - xMin) * width, top + height - (value - min) / (max - min) * height);
        for (var tick = 0; tick <= 4; tick++)
        {
            var value = min + (max - min) * tick / 4;
            var y = top + height - height * tick / 4;
            dc.DrawLine(new Pen(Brushes.LightGray, 0.6), new Point(left, y), new Point(left + width, y));
            Text(value.ToString("0.##", CultureInfo.InvariantCulture), 8, y - 7);
        }
        if (!Original) dc.DrawLine(new Pen(Brushes.DarkGray, 1.5), Position(Math.Pow(10, xMin), 0), Position(Math.Pow(10, xMax), 0));
        for (var tick = 0; tick <= 4; tick++)
        {
            var x = left + width * tick / 4;
            Text(Math.Pow(10, xMin + (xMax - xMin) * tick / 4).ToString("0.#", CultureInfo.InvariantCulture), x - 10, top + height + 8);
        }
        Text("Hz（对数坐标）；缺失处断线，不插值。", left, ActualHeight - 20);
        void Series(Func<BandDifference, double?> get, Brush brush)
        {
            Point? previous = null;
            foreach (var band in bands)
            {
                var value = get(band);
                if (!value.HasValue) { previous = null; continue; }
                var point = Position(band.Frequency, value.Value);
                if (previous.HasValue) dc.DrawLine(new Pen(brush, 1.8), previous.Value, point);
                dc.DrawEllipse(brush, null, point, 3, 3);
                previous = point;
            }
        }
        if (Original) { Series(item => item.Baseline, teal); Series(item => item.Target, copper); }
        else Series(item => item.Difference, teal);
    }
}
