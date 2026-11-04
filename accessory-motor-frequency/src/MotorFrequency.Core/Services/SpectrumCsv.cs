using System.Globalization;
using System.Text;
using MotorFrequency.Core.Domain;

namespace MotorFrequency.Core.Services;

public static class SpectrumCsv
{
    public const int MaximumBytes = 1024 * 1024;

    public static CsvImportResult Parse(string content, SpectrumMetadata metadata)
    {
        var issues = new List<ValidationIssue>();
        var points = new List<SpectrumPoint>();
        if (Encoding.UTF8.GetByteCount(content) > MaximumBytes)
        {
            return new(null, [new("file", "CSV 超过 1 MiB；请减少频点。")], 0);
        }
        var lines = content.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length < 3)
        {
            return new(null, [new("file", "需表头和至少两行频谱点。")], 0);
        }
        if (lines[0].Trim() != "frequency_hz,amplitude")
        {
            return new(null, [new("line 1", "表头必须为 frequency_hz,amplitude，使用英文逗号。")], 0);
        }
        for (var index = 1; index < lines.Length; index++)
        {
            if (issues.Count >= 25)
            {
                issues.Add(new("file", "错误达到25条，停止解析；整条频谱未导入。"));
                break;
            }
            if (string.IsNullOrWhiteSpace(lines[index]))
            {
                if (index < lines.Length - 1 && lines.Skip(index).Any(x => !string.IsNullOrWhiteSpace(x)))
                {
                    issues.Add(new("line " + (index + 1), "中间空行不接受；请移除后重试。"));
                }
                continue;
            }
            var cells = lines[index].Split(',');
            if (cells.Length != 2)
            {
                issues.Add(new("line " + (index + 1), "每行必须正好两列，不支持千位分隔符。"));
                continue;
            }
            var style = NumberStyles.Float;
            var culture = CultureInfo.InvariantCulture;
            if (!double.TryParse(cells[0], style, culture, out var hz) || !double.IsFinite(hz))
            {
                issues.Add(new("line " + (index + 1) + ", frequency_hz", "频率必须为有限数值，单位 Hz，小数点使用英文句点。"));
                continue;
            }
            if (!double.TryParse(cells[1], style, culture, out var amplitude) || !double.IsFinite(amplitude))
            {
                issues.Add(new("line " + (index + 1) + ", amplitude", "幅值必须为有限数值。"));
                continue;
            }
            points.Add(new SpectrumPoint { FrequencyHz = hz, Amplitude = amplitude });
            if (issues.Count >= 25 || points.Count > 10000)
            {
                issues.Add(new("file", "超过 25 条错误或 10000 点，停止解析。"));
                break;
            }
        }
        var spectrum = new Spectrum
        {
            Name = metadata.Name,
            Rpm = metadata.Rpm,
            Unit = metadata.Unit,
            Reference = metadata.Reference,
            Condition = metadata.Condition,
            Origin = metadata.Origin,
            FileName = metadata.FileName,
            InputSha256 = ProjectCodec.Hash(content),
            Points = points
        };
        issues.AddRange(ProjectValidator.ValidateSpectrum(spectrum));
        return new(issues.Count == 0 ? spectrum : null, issues, points.Count);
    }

    public static string Template()
    {
        return "frequency_hz,amplitude\n0,20\n30,25\n59,32\n60,48\n61,33\n90,21\n";
    }

    public static string Export(Spectrum spectrum)
    {
        var text = new StringBuilder("frequency_hz,amplitude\n");
        foreach (var point in spectrum.Points)
        {
            text.Append(point.FrequencyHz.ToString("R", CultureInfo.InvariantCulture));
            text.Append(',');
            text.AppendLine(point.Amplitude.ToString("R", CultureInfo.InvariantCulture));
        }
        return text.ToString();
    }
}
