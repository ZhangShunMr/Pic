using System.Globalization;
using System.Text;
using Blower.Core.Domain;

namespace Blower.Core.IO;

public sealed record CsvProblem(int Line, string Column, string Message);
public sealed record CsvPreview(string Kind, string InputHash, IReadOnlyList<CsvProblem> Problems,
    IReadOnlyList<Gear> Gears, IReadOnlyList<Mode> Modes)
{
    public bool Valid => Problems.Count == 0 && Gears.Count + Modes.Count > 0;
}

public static class CsvExchange
{
    public static string Cell(string? value)
    {
        value ??= "";
        var numeric = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number);
        if (!numeric && value.Length > 0 && "=+-@\t\r".Contains(value[0]))
        {
            value = "'" + value;
        }
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public static string Number(double value)
    {
        return value.ToString("0.########", CultureInfo.InvariantCulture);
    }

    public static string Row(params string?[] values)
    {
        return string.Join(",", values.Select(Cell));
    }

    public static IReadOnlyList<string> ParseRow(string line)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var closed = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                    closed = true;
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == ',')
            {
                cells.Add(cell.ToString());
                cell.Clear();
                closed = false;
            }
            else if (c == '"' && cell.Length == 0 && !closed)
            {
                quoted = true;
            }
            else if (closed || c == '"')
            {
                throw new InvalidDataException("引号必须包围整个单元格；不支持跨行单元格");
            }
            else
            {
                cell.Append(c);
            }
        }
        if (quoted)
        {
            throw new InvalidDataException("引号未闭合；不支持跨行单元格");
        }
        cells.Add(cell.ToString());
        return cells;
    }

    public static CsvPreview Preview(string text, string kind, Project project)
    {
        var problems = new List<CsvProblem>();
        var gears = new List<Gear>();
        var modes = new List<Mode>();
        var hash = ProjectCodec.InputHash(project);
        var rows = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Split('\n');
        var expected = kind == "gears" ? "name,rpm,fixed" : "name,frequency_hz,damping_ratio,source";
        if (rows.Length == 0 || rows[0].TrimEnd('\r') != expected)
        {
            problems.Add(new(1, "表头", "必须为 " + expected));
            return new(kind, hash, problems, gears, modes);
        }
        var limit = kind == "gears" ? 24 : 100;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < rows.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(rows[i]))
            {
                continue;
            }
            if (gears.Count + modes.Count >= limit)
            {
                problems.Add(new(i + 1, "行数", "超过对象数量上限"));
                break;
            }
            try
            {
                var cells = ParseRow(rows[i].TrimEnd('\r'));
                if (cells.Count != (kind == "gears" ? 3 : 4))
                {
                    throw new InvalidDataException("列数不符；使用英文逗号和小数点");
                }
                if (string.IsNullOrWhiteSpace(cells[0]) || !names.Add(cells[0]))
                {
                    throw new InvalidDataException("名称为空或重复");
                }
                if (!double.TryParse(cells[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    || !double.IsFinite(value))
                {
                    throw new InvalidDataException("第二列须为有限数值");
                }
                if (kind == "gears")
                {
                    if (!bool.TryParse(cells[2], out var fixedGear))
                    {
                        throw new InvalidDataException("fixed只接受true或false");
                    }
                    gears.Add(new() { Name = cells[0], Rpm = value, Fixed = fixedGear });
                }
                else
                {
                    double? damping = null;
                    if (!string.IsNullOrWhiteSpace(cells[2]))
                    {
                        if (!double.TryParse(cells[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                            || !double.IsFinite(d))
                        {
                            throw new InvalidDataException("阻尼比须为小数，未知留空");
                        }
                        damping = d;
                    }
                    modes.Add(new()
                    {
                        Name = cells[0], FrequencyHz = value, DampingRatio = damping,
                        Source = cells[3], Origin = project.Origin
                    });
                }
            }
            catch (InvalidDataException error)
            {
                problems.Add(new(i + 1, "内容", error.Message));
            }
        }
        if (gears.Count + modes.Count == 0 && problems.Count == 0)
        {
            problems.Add(new(2, "内容", "至少提供一个对象"));
        }
        var candidate = ProjectCodec.Clone(project);
        if (kind == "gears")
        {
            candidate.Plans = [new() { Name = "CSV预检", Gears = gears }];
            candidate.Checks = [];
        }
        else
        {
            candidate.Modes = modes;
            candidate.Checks = [];
        }
        foreach (var issue in Validation.Inspect(candidate).Where(x => x.Blocks))
        {
            problems.Add(new(0, issue.Path, issue.Message));
        }
        return new(kind, hash, problems, gears, modes);
    }
}
