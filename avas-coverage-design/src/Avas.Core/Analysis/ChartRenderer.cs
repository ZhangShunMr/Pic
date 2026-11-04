using System.Globalization;
using System.Net;
using System.Text;
using Avas.Core.Domain;

namespace Avas.Core.Analysis;

public static class ChartRenderer
{
    public static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    public static string Escape(string value) => WebUtility.HtmlEncode(value);

    public static string Color(double value, double min, double max)
    {
        var ratio = Math.Clamp((value - min) / Math.Max(1, max - min), 0, 1);
        var stops = new (int R, int G, int B)[] { (31, 61, 115), (28, 127, 150), (83, 183, 140), (235, 208, 100), (220, 93, 71) };
        var position = ratio * (stops.Length - 1);
        var left = Math.Min(stops.Length - 2, (int)position);
        var fraction = position - left;
        var a = stops[left];
        var b = stops[left + 1];
        return $"#{(int)(a.R + (b.R - a.R) * fraction):x2}{(int)(a.G + (b.G - a.G) * fraction):x2}{(int)(a.B + (b.B - a.B) * fraction):x2}";
    }

    public static string Map(Project project, Calculation? result, bool coverageOnly = false)
    {
        var settings = project.Settings;
        if (settings.MaxX <= settings.MinX || settings.MaxY <= settings.MinY)
            return "<svg viewBox='0 0 900 620'><text x='30' y='40'>请修正网格坐标范围</text></svg>";
        const double width = 900;
        const double height = 620;
        const double margin = 65;
        var scale = Math.Min((width - margin * 2) / (settings.MaxX - settings.MinX), (height - margin * 2) / (settings.MaxY - settings.MinY));
        double X(double x) => margin + (x - settings.MinX) * scale;
        double Y(double y) => height - margin - (y - settings.MinY) * scale;
        var svg = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 900 620' role='img' aria-label='车辆周围声场布局，X正向为车前，Y正向为车左'>");
        svg.Append("<rect width='900' height='620' fill='#f5f7fa'/>");
        svg.Append($"<metadata>{Escape(IO.ProjectCodec.Json(new { Software.Model, Software.Version, Boundary = Software.Boundary, project.Name, Source = IO.ProjectCodec.SourceSummary(project), Conditions = IO.ProjectCodec.Conditions(project.Settings), InputHash = IO.ProjectCodec.InputHash(project) }))}</metadata>");
        svg.Append($"<text x='25' y='27' font-size='15' fill='#24344d'>{Escape(result?.Metric ?? "布局预览，尚无有效计算结果")}</text>");
        if (result is not null)
        {
            var min = Math.Floor(result.Minimum / 5) * 5;
            var max = Math.Max(min + 5, Math.Ceiling(result.Maximum / 5) * 5);
            var half = settings.Step * scale / 2;
            foreach (var point in result.Grid)
            {
                var color = coverageOnly ? point.Covered ? "#4aa47b" : "#e7b66a" : Color(point.Signal, min, max);
                svg.Append($"<rect x='{Number(X(point.X) - half)}' y='{Number(Y(point.Y) - half)}' width='{Number(half * 2 + 0.2)}' height='{Number(half * 2 + 0.2)}' fill='{color}'><title>{Number(point.X)},{Number(point.Y)}m：{Number(point.Signal)}dB；背景差{Number(point.Margin)}dB；{(point.Covered ? "满足工程目标" : "未满足工程目标")}</title></rect>");
            }
            if (!coverageOnly)
            {
                for (var i = 0; i < 100; i++)
                    svg.Append($"<rect x='{730 + i}' y='580' width='1.5' height='12' fill='{Color(min + (max - min) * i / 99, min, max)}'/>");
                svg.Append($"<text x='730' y='610' font-size='11'>{Number(min)}—{Number(max)}dB</text>");
            }
        }
        for (var x = Math.Ceiling(settings.MinX / 2) * 2; x <= settings.MaxX; x += 2)
        {
            svg.Append($"<line x1='{Number(X(x))}' y1='{Number(Y(settings.MinY))}' x2='{Number(X(x))}' y2='{Number(Y(settings.MaxY))}' stroke='#ffffff' stroke-opacity='.35'/>");
            svg.Append($"<text x='{Number(X(x))}' y='{Number(Y(settings.MinY) + 24)}' text-anchor='middle' font-size='12'>{Number(x)}</text>");
        }
        for (var y = Math.Ceiling(settings.MinY / 2) * 2; y <= settings.MaxY; y += 2)
        {
            svg.Append($"<line x1='{Number(X(settings.MinX))}' y1='{Number(Y(y))}' x2='{Number(X(settings.MaxX))}' y2='{Number(Y(y))}' stroke='#ffffff' stroke-opacity='.35'/>");
            svg.Append($"<text x='{Number(X(settings.MinX) - 14)}' y='{Number(Y(y) + 4)}' text-anchor='end' font-size='12'>{Number(y)}</text>");
        }
        svg.Append($"<rect x='{Number(X(-settings.VehicleLength / 2))}' y='{Number(Y(settings.VehicleWidth / 2))}' width='{Number(settings.VehicleLength * scale)}' height='{Number(settings.VehicleWidth * scale)}' rx='12' fill='#e9edf4' stroke='#33465e' stroke-width='2'/>");
        svg.Append($"<text x='{Number(X(0))}' y='{Number(Y(0) + 5)}' text-anchor='middle' font-size='13' fill='#253b56'>车辆 →</text>");
        foreach (var speaker in project.Speakers)
        {
            var x = X(speaker.X);
            var y = Y(speaker.Y);
            var angle = speaker.Heading * Math.PI / 180;
            var x2 = x + Math.Cos(angle) * 27;
            var y2 = y - Math.Sin(angle) * 27;
            svg.Append($"<line x1='{Number(x)}' y1='{Number(y)}' x2='{Number(x2)}' y2='{Number(y2)}' stroke='#172e4d' stroke-width='3'/>");
            svg.Append($"<circle cx='{Number(x)}' cy='{Number(y)}' r='7' fill='{(speaker.Enabled ? "#fdfdfd" : "#aaa")}' stroke='#172e4d' stroke-width='3'><title>{Escape(speaker.Name)}，朝向{Number(speaker.Heading)}°</title></circle>");
        }
        foreach (var point in project.Listeners)
        {
            svg.Append($"<circle cx='{Number(X(point.X))}' cy='{Number(Y(point.Y))}' r='5' fill='#fff' stroke='#111' stroke-width='2'/>");
            svg.Append($"<text x='{Number(X(point.X) + 9)}' y='{Number(Y(point.Y) - 8)}' font-size='12' fill='#16273f' paint-order='stroke' stroke='#fff' stroke-width='3'>{Escape(point.Name)}</text>");
        }
        svg.Append("<text x='65' y='607' font-size='12' fill='#24344d'>X / m →车前　Y / m ↑车左　圆圈为扬声器与观察点；轮廓不参与遮挡计算</text>");
        svg.Append("</svg>");
        return svg.ToString();
    }

    public static string Spectrum(PointResult point, Settings settings)
    {
        var background = Software.Frequencies.Select(hz => settings.Background.Single(x => x.Hz == hz).Level).ToArray();
        var all = point.Bands.Concat(background).ToArray();
        var min = Math.Floor(all.Min() / 10) * 10 - 10;
        var max = Math.Ceiling(all.Max() / 10) * 10 + 10;
        double Y(double value) => 240 - (value - min) / (max - min) * 200;
        var svg = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 760 300' role='img' aria-label='观察点七频带声压级与背景声对比'>");
        svg.Append("<rect width='760' height='300' fill='#fff'/>");
        for (var value = min; value <= max; value += 10)
        {
            svg.Append($"<line x1='55' y1='{Number(Y(value))}' x2='730' y2='{Number(Y(value))}' stroke='#e1e6ed'/>");
            svg.Append($"<text x='46' y='{Number(Y(value) + 4)}' text-anchor='end' font-size='12'>{Number(value)}</text>");
        }
        var signalPoints = new List<string>();
        var backgroundPoints = new List<string>();
        for (var i = 0; i < 7; i++)
        {
            var x = 85 + i * 100;
            signalPoints.Add($"{x},{Number(Y(point.Bands[i]))}");
            backgroundPoints.Add($"{x},{Number(Y(background[i]))}");
            svg.Append($"<circle cx='{x}' cy='{Number(Y(point.Bands[i]))}' r='4' fill='#146d81'/>");
            svg.Append($"<text x='{x}' y='263' text-anchor='middle' font-size='12'>{Software.Frequencies[i]}</text>");
        }
        svg.Append($"<polyline points='{string.Join(" ", signalPoints)}' fill='none' stroke='#146d81' stroke-width='3'/>");
        svg.Append($"<polyline points='{string.Join(" ", backgroundPoints)}' fill='none' stroke='#b47936' stroke-width='2' stroke-dasharray='6 4'/>");
        svg.Append("<text x='55' y='20' font-size='12'>未计权频带声压级 / dB re 20µPa</text><text x='400' y='290' font-size='12'>中心频率 / Hz　蓝实线：警示声　棕虚线：背景</text></svg>");
        return svg.ToString();
    }
}
