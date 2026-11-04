using System.Windows;
using System.Windows.Media;

using TrimContact.Core.Domain;
namespace TrimContact.Desktop.Controls;

public sealed class PhasePlot : WorkspaceDrawing
{
    private static readonly Brush[] SeriesBrushes =
    [ Brushes.Teal, Brushes.IndianRed, Brushes.SteelBlue, Brushes.DarkGoldenrod, Brushes.Purple, Brushes.DarkOliveGreen ];

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Brushes.White, null, new Rect(RenderSize));
        if (Workspace is null || ActualHeight < 160 || ActualWidth < 240) return;
        var result = Workspace.VariantResults.FirstOrDefault(v => v.VariantId == Workspace.SelectedVariant?.Id);
        if (result is null)
        {
            Text(dc, "无有效结果。应用输入后求解全部方案。", 20, 30);
            return;
        }
        var traces = result.NominalTraces;
        if (traces.Count == 0) return;
        var rowHeight = ActualHeight / traces.Count;
        for (int traceIndex = 0; traceIndex < traces.Count; traceIndex++)
        {
            var trace = traces[traceIndex];
            var top = traceIndex * rowHeight;
            var chart = new Rect(70, top + 35, Math.Max(1, ActualWidth - 100), Math.Max(20, rowHeight - 110));
            var contacts = trace.Samples.FirstOrDefault()?.Contacts;
            var count = contacts?.Count ?? 0;
            Text(dc, trace.LoadName + " · 名义公差 · " + Workspace.Draft.Origin.Label(), 15, top + 5);
            var maximum = trace.Samples.SelectMany(s => s.Contacts.Select(c => c.CompressionMm)).DefaultIfEmpty(0).Max();
            maximum = Math.Max(0.01, maximum * 1.1);
            dc.DrawLine(new Pen(Brushes.Gray, 1), chart.BottomLeft, chart.TopLeft);
            dc.DrawLine(new Pen(Brushes.Gray, 1), chart.BottomLeft, chart.BottomRight);
            Text(dc, "压缩 mm", chart.Left, top + 20);
            for (int tick = 0; tick <= 4; tick++)
            {
                var y = chart.Bottom - chart.Height * tick / 4;
                dc.DrawLine(new Pen(Brushes.WhiteSmoke, 1), new Point(chart.Left, y), new Point(chart.Right, y));
                Text(dc, N(maximum * tick / 4), 8, y - 8, size: 10);
                Text(dc, (tick * 90).ToString() + "°", chart.Left + chart.Width * tick / 4 - 10, chart.Bottom + 3, size: 10);
            }
            for (int contactIndex = 0; contactIndex < count; contactIndex++)
            {
                var brush = SeriesBrushes[contactIndex % SeriesBrushes.Length];
                Point? previous = null;
                foreach (var sample in trace.Samples)
                {
                    var value = sample.Contacts[contactIndex].CompressionMm;
                    var point = new Point(chart.Left + chart.Width * sample.PhaseDegrees / 360,
                        chart.Bottom - chart.Height * value / maximum);
                    if (previous.HasValue) dc.DrawLine(new Pen(brush, 1.8), previous.Value, point);
                    dc.DrawEllipse(brush, null, point, 2.2, 2.2);
                    previous = point;
                }
                var columns = Math.Max(1, (int)(chart.Width / 135));
                Text(dc, contacts![contactIndex].Name, chart.Left + (contactIndex % columns) * 135,
                    chart.Bottom + 23 + contactIndex / columns * 16, brush, 11);
            }
            if (count == 0) Text(dc, "此方案没有单边接触点；请查看卡扣包络与名义相位CSV。", chart.Left + 10, chart.Top + 15);
        }
    }
}
