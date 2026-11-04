using System.Globalization;
using System.Text;
using Avas.Core.Domain;

namespace Avas.Core.IO;

public sealed class SpectrumPreview
{
    public List<BandInput> Bands { get; init; } = [];
    public List<string> Errors { get; init; } = [];
    public bool Valid => Errors.Count == 0 && Bands.Count == 7;
}

public static class SpectrumCsv
{
    public const string Header = "FrequencyHz,LevelDb,CorrectionDb";

    public static string Template(bool background = false)
    {
        return Header + "\n" + string.Join("\n", Software.Frequencies.Select(hz => $"{hz},{(background ? 35 : 60)},0"));
    }

    public static SpectrumPreview Parse(string text, bool background = false)
    {
        var errors = new List<string>();
        var bands = new List<BandInput>();
        if (text.Length > 20000)
            return new SpectrumPreview { Errors = ["文件超过20000字符限制"] };
        var lines = text.TrimStart('\uFEFF').Replace("\r", "").Split('\n');
        if (lines.Length == 0 || lines[0] != Header)
            errors.Add("第1行表头必须为 " + Header + "；使用UTF-8与英文逗号");
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;
            var fields = lines[i].Split(',');
            if (fields.Length != 3)
            {
                errors.Add($"第{i + 1}行必须恰好三列，不支持千分位或引号");
                continue;
            }
            if (!int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var hz)
                || !Software.Frequencies.Contains(hz))
            {
                errors.Add($"第{i + 1}行频率无效，仅支持七个倍频程中心频率");
                continue;
            }
            if (!double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var level)
                || !double.IsFinite(level) || level is < -60 or > 140)
            {
                errors.Add($"第{i + 1}行LevelDb必须为-60至140的有限数值");
                continue;
            }
            if (!double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var correction)
                || !double.IsFinite(correction) || correction is < -40 or > 40 || (background && correction != 0))
            {
                errors.Add($"第{i + 1}行CorrectionDb无效；声源范围-40至40，背景必须为0");
                continue;
            }
            if (bands.Any(x => x.Hz == hz))
            {
                errors.Add($"第{i + 1}行频率{hz}重复");
                continue;
            }
            bands.Add(new BandInput { Hz = hz, Level = level, Correction = correction });
        }
        foreach (var hz in Software.Frequencies.Except(bands.Select(x => x.Hz)))
            errors.Add($"缺少{hz}Hz，不能自动补零");
        return new SpectrumPreview { Bands = bands.OrderBy(x => x.Hz).ToList(), Errors = errors };
    }

    public static byte[] Write(IEnumerable<BandInput> bands)
    {
        var rows = bands.OrderBy(x => x.Hz).Select(x =>
            FormattableString.Invariant($"{x.Hz},{x.Level:R},{x.Correction:R}"));
        return Encoding.UTF8.GetBytes("\uFEFF" + Header + "\r\n" + string.Join("\r\n", rows));
    }

    public static string Cell(string? value)
    {
        var safe = value ?? "";
        if (safe.Length > 0 && "=+-@\t\r".Contains(safe[0]))
            safe = "'" + safe;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }
}
