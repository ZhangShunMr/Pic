namespace Cabin.Desktop.ViewModels;

public sealed record PlotPoint(double X, double Y);
public sealed record PlotLine(string Name, string Color, IReadOnlyList<PlotPoint> Points);
public sealed record PlotData(string Title, string XLabel, string YLabel, IReadOnlyList<PlotLine> Lines);
public sealed record Choice(string Key, string Label)
{
    public override string ToString() => Label;
}
