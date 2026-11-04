using System.Globalization;
using System.Text;
using Avas.Core.Domain;

namespace Avas.Core.IO;

public sealed record ListenerPreview(List<Listener> Points, List<string> Errors)
{
    public bool Valid => Errors.Count == 0 && Points.Count > 0;
}

public static class ListenerCsv
{
    public const string Header = "Name,X_m,Y_m,Note";

    public static string Template() => Header + "\n车前5m,7.3,0,人工定义观察点\n左前点,5,4,人工定义观察点";

    public static ListenerPreview Parse(string text)
    {
        var points = new List<Listener>();
        var errors = new List<string>();
        if (text.Length > 100000)
            return new(points, ["观察点CSV最多100000字符"]);
        var lines = text.TrimStart('\uFEFF').Replace("\r", "").Split('\n');
        if (lines.Length == 0 || lines[0] != Header)
            errors.Add("第1行表头必须为 " + Header);
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;
            List<string> fields;
            try
            {
                fields = SplitLine(lines[i]);
            }
            catch (ArgumentException error)
            {
                errors.Add($"第{i + 1}行：{error.Message}");
                continue;
            }
            if (fields.Count != 4)
            {
                errors.Add($"第{i + 1}行：应有名称、X、Y、备注四列");
                continue;
            }
            if (string.IsNullOrWhiteSpace(fields[0]) || fields[0].Length > 100 || fields[3].Length > 2000)
            {
                errors.Add($"第{i + 1}行：名称必填且不超过100字符，备注不超过2000字符");
                continue;
            }
            if (!double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
                || !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var y)
                || !double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 100 || Math.Abs(y) > 100)
            {
                errors.Add($"第{i + 1}行：X/Y必须是-100至100m的有限数值");
                continue;
            }
            if (points.Any(p => p.Name == fields[0]))
            {
                errors.Add($"第{i + 1}行：名称与前行重复，无法清楚区分观察点");
                continue;
            }
            points.Add(new Listener { Name = fields[0], X = x, Y = y, Note = fields[3] });
        }
        if (points.Count is < 1 or > 100)
            errors.Add("有效观察点必须为1至100个");
        return new(points, errors);
    }

    public static List<string> SplitLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
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
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                        closed = true;
                    }
                }
                else
                    current.Append(c);
            }
            else if (c == ',')
            {
                fields.Add(current.ToString());
                current.Clear();
                closed = false;
            }
            else if (closed)
                throw new ArgumentException("结束引号之后只能接逗号或行尾");
            else if (c == '"')
            {
                if (current.Length != 0)
                    throw new ArgumentException("引号只能出现在字段开头");
                quoted = true;
            }
            else
                current.Append(c);
        }
        if (quoted)
            throw new ArgumentException("引号未闭合；不支持字段内换行");
        fields.Add(current.ToString());
        return fields;
    }

    public static byte[] Write(IEnumerable<Listener> points)
    {
        var text = new StringBuilder("\uFEFF" + Header + "\r\n");
        foreach (var point in points)
        {
            var name = point.Name.Replace("\r", " ").Replace("\n", " ");
            var note = point.Note.Replace("\r", " ").Replace("\n", " ");
            text.AppendLine(string.Join(",", Quote(name), point.X.ToString("R", CultureInfo.InvariantCulture),
                point.Y.ToString("R", CultureInfo.InvariantCulture), Quote(note)));
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static string Quote(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
}
