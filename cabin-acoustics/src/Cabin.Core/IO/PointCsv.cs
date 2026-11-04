using System.Globalization;
using System.Text;
using Cabin.Core.Domain;

namespace Cabin.Core.IO;

public sealed record PointRow(string Kind, string Name, Position Point, double Amplitude, double Phase, string Note);
public sealed class CsvPreview
{
    public List<PointRow> Rows { get; } = [];
    public List<Issue> Issues { get; } = [];
    public bool CanApply => Rows.Count > 0 && Issues.Count == 0;
}

public static class PointCsv
{
    public const string Header = "kind,name,x_m,y_m,z_m,amplitude,phase_deg,note";

    public static CsvPreview Preview(string text, Cavity model)
    {
        var preview = new CsvPreview();
        if (text.Length > 100000)
        {
            preview.Issues.Add(new("CSV", "点位CSV最多100000字符"));
            return preview;
        }
        var lines = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n');
        if (lines.Length == 0 || lines[0].Trim() != Header)
        {
            preview.Issues.Add(new("第1行", "表头必须为：" + Header));
            return preview;
        }
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;
            var path = "第" + (i + 1) + "行";
            try
            {
                var cells = ParseLine(lines[i]);
                if (cells.Count != 8)
                    throw new ArgumentException("需要8列");
                if (cells[0] is not ("source" or "seat"))
                    throw new ArgumentException("kind必须为source或seat");
                var point = new Position(Number(cells[2]), Number(cells[3]), Number(cells[4]));
                var amplitude = cells[0] == "source" ? Number(cells[5]) : 0;
                var phase = cells[0] == "source" ? Number(cells[6]) : 0;
                var row = new PointRow(cells[0], cells[1], point, amplitude, phase, cells[7]);
                var issues = new List<Issue>();
                var sources = row.Kind == "source" ? new List<Emitter> { new() { Name = row.Name, Point = row.Point, Amplitude = amplitude, PhaseDegrees = phase } } : [];
                var seats = row.Kind == "seat" ? new List<EarPoint> { new() { Name = row.Name, Point = row.Point, Note = row.Note } } : [];
                Validation.Points(model, sources, seats, issues);
                if (issues.Count > 0)
                    throw new ArgumentException(string.Join("；", issues.Select(x => x.Path + "：" + x.Message)));
                preview.Rows.Add(row);
            }
            catch (ArgumentException error)
            {
                preview.Issues.Add(new(path, error.Message));
            }
        }
        if (preview.Rows.Count(x => x.Kind == "source") > 8 || preview.Rows.Count(x => x.Kind == "seat") > 12)
            preview.Issues.Add(new("CSV", "最多8个声源、12个耳点"));
        if (preview.Rows.Count == 0)
            preview.Issues.Add(new("CSV", "没有有效点位"));
        return preview;
    }

    public static void Apply(Project project, CsvPreview preview)
    {
        if (!preview.CanApply)
            throw new ArgumentException("CSV预检未通过；项目未改变");
        var sources = new List<Emitter>();
        var seats = new List<EarPoint>();
        foreach (var row in preview.Rows)
        {
            if (row.Kind == "source")
                sources.Add(new() { Name = row.Name, Point = ProjectCodec.Clone(row.Point), Amplitude = row.Amplitude, PhaseDegrees = row.Phase });
            else
                seats.Add(new() { Name = row.Name, Point = ProjectCodec.Clone(row.Point), Note = row.Note });
        }
        project.Sources = sources;
        project.Seats = seats;
    }

    public static string Export(Project project)
    {
        var builder = new StringBuilder(Header + Environment.NewLine);
        foreach (var source in project.Sources)
            builder.AppendLine(Join("source", source.Name, N(source.Point.X), N(source.Point.Y), N(source.Point.Z), N(source.Amplitude), N(source.PhaseDegrees), source.Enabled ? "启用" : "停用；导入时需手动停用"));
        foreach (var seat in project.Seats)
            builder.AppendLine(Join("seat", seat.Name, N(seat.Point.X), N(seat.Point.Y), N(seat.Point.Z), "", "", seat.Note));
        return builder.ToString();
    }

    public static string Join(params string[] fields) => string.Join(",", fields.Select(Quote));
    public static string N(double value) => value.ToString("G12", CultureInfo.InvariantCulture);
    public static string Quote(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    public static List<string> ParseLine(string line)
    {
        var cells = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        var closed = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        value.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                        closed = true;
                    }
                }
                else
                    value.Append(c);
            }
            else if (c == ',')
            {
                cells.Add(value.ToString());
                value.Clear();
                closed = false;
            }
            else if (c == '"' && value.Length == 0 && !closed)
                quoted = true;
            else if (closed || c == '"')
                throw new ArgumentException("引号格式错误；不支持单元格内换行");
            else
                value.Append(c);
        }
        if (quoted)
            throw new ArgumentException("引号未闭合；不支持单元格内换行");
        cells.Add(value.ToString());
        return cells;
    }

    private static double Number(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
            throw new ArgumentException("数值使用小数点，不能为空或非有限值");
        return number;
    }
}
