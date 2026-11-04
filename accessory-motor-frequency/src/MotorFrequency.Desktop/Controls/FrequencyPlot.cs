using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MotorFrequency.Core.Domain;
using MotorFrequency.Core.Services;

namespace MotorFrequency.Desktop.Controls;

public sealed class FrequencyPlot : PlotBase
{
    public static readonly DependencyProperty ProjectProperty = DependencyProperty.Register(
        nameof(Project), typeof(Project), typeof(FrequencyPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SnapshotProperty = DependencyProperty.Register(
        nameof(Snapshot), typeof(Scenario), typeof(FrequencyPlot), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty InspectCommandProperty = DependencyProperty.Register(
        nameof(InspectCommand), typeof(ICommand), typeof(FrequencyPlot));
    public Project? Project { get => (Project?)GetValue(ProjectProperty); set => SetValue(ProjectProperty, value); }
    public Scenario? Snapshot { get => (Scenario?)GetValue(SnapshotProperty); set => SetValue(SnapshotProperty, value); }
    public ICommand? InspectCommand { get => (ICommand?)GetValue(InspectCommandProperty); set => SetValue(InspectCommandProperty, value); }

    public FrequencyPlot()
    {
        Cursor = Cursors.Cross;
        ToolTip = "横轴：r/min；纵轴：Hz。点击绘图区移动观察转速。圆点大小不代表声级或幅值。";
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (Project is null)
        {
            Empty(drawing, "建立项目后显示候选频率图。");
            return;
        }
        var motor = Project.Motor;
        Axes(drawing, motor.MinimumRpm, motor.MaximumRpm, 0, motor.MaximumHz, "转速 r/min", "频率 Hz");
        var area = PlotArea;
        double X(double rpm) => area.Left + (rpm - motor.MinimumRpm) / (motor.MaximumRpm - motor.MinimumRpm) * area.Width;
        double Y(double hz) => area.Bottom - hz / motor.MaximumHz * area.Height;
        drawing.PushClip(new RectangleGeometry(area));
        foreach (var band in Project.Bands.Where(x => x.Enabled && x.LowerHz <= motor.MaximumHz))
        {
            var upper = Math.Min(band.UpperHz, motor.MaximumHz);
            var top = Y(upper);
            var height = Math.Max(1, Y(band.LowerHz) - top);
            drawing.DrawRectangle(new SolidColorBrush(Color.FromArgb(48, 199, 143, 42)), null,
                new Rect(area.Left, top, area.Width, height));
        }
        var candidates = FrequencyEngine.Candidates(motor, Project.CustomOrders).Where(x => x.Visible).ToArray();
        for (var index = 0; index < candidates.Length; index++)
        {
            DrawCandidate(drawing, candidates[index], new Pen(ColorBrush(ChartGeometry.CandidateColor(candidates[index].Id)), 2), X, Y);
        }
        if (Snapshot is not null)
        {
            foreach (var candidate in FrequencyEngine.Candidates(Snapshot.Motor, Snapshot.CustomOrders).Where(x => x.Visible))
            {
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(150, 73, 93, 121)), 1.5) { DashStyle = DashStyles.Dash };
                DrawCandidate(drawing, candidate, pen, X, Y);
            }
        }
        var inspect = X(motor.InspectRpm);
        drawing.DrawLine(new Pen(Ink, 1) { DashStyle = DashStyles.Dot }, new Point(inspect, area.Top), new Point(inspect, area.Bottom));
        foreach (var spectrum in Project.Spectra.Where(x => x.Visible && x.Rpm >= motor.MinimumRpm && x.Rpm <= motor.MaximumRpm))
        {
            foreach (var point in FrequencyEngine.MatchPeaks(Project, spectrum).Select(x => x.ObservedHz).Distinct().Where(x => x <= motor.MaximumHz))
            {
                drawing.DrawEllipse(Brushes.White, new Pen(Ink, 1.5), new Point(X(spectrum.Rpm), Y(point)), 3.5, 3.5);
            }
        }
        drawing.Pop();
        Text(drawing, Project.Demonstration ? "模拟演示 · 候选频率参考" : "人工参数 · 候选频率参考", new Point(area.Left + 5, 7), Muted);
    }

    private void DrawCandidate(DrawingContext drawing, Candidate candidate, Pen pen,
        Func<double, double> x, Func<double, double> y)
    {
        var motor = Project!.Motor;
        var lower = motor.MinimumRpm;
        var upper = Math.Min(motor.MaximumRpm, 60 * motor.MaximumHz / candidate.Order);
        if (upper < lower)
        {
            return;
        }
        drawing.DrawLine(pen, new Point(x(lower), y(FrequencyEngine.At(candidate.Order, lower))),
            new Point(x(upper), y(FrequencyEngine.At(candidate.Order, upper))));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var point = e.GetPosition(this);
        if (Project is null || !PlotArea.Contains(point))
        {
            return;
        }
        var motor = Project.Motor;
        var rpm = motor.MinimumRpm + (point.X - PlotArea.Left) / PlotArea.Width * (motor.MaximumRpm - motor.MinimumRpm);
        rpm = Math.Clamp(Math.Round(rpm, 2), motor.MinimumRpm, motor.MaximumRpm);
        if (InspectCommand?.CanExecute(rpm) == true)
        {
            InspectCommand.Execute(rpm);
            e.Handled = true;
        }
    }
}
