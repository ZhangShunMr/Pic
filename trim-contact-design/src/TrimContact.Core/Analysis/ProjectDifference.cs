using System.Globalization;
using TrimContact.Core.Domain;
using TrimContact.Core.IO;

namespace TrimContact.Core.Analysis;

public sealed record InputDifference(string Path, string Current, string Reference);

public sealed record DifferenceReport(string CurrentHash, string ReferenceHash, string ReferenceName,
    bool SameSharedConditions, IReadOnlyList<InputDifference> Rows);

public static class ProjectDifference
{
    private static string N(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    private static Dictionary<string, string> Flatten(DesignProject project)
    {
        var rows = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["项目/名称"] = project.Name,
            ["项目/修订"] = project.Revision,
            ["共同条件/来源类型"] = project.Origin.Label(),
            ["共同条件/来源依据"] = project.Source,
            ["共同条件/质量kg"] = N(project.Panel.MassKg),
            ["共同条件/质心Xmm"] = N(project.Panel.CenterXmm),
            ["共同条件/质心Ymm"] = N(project.Panel.CenterYmm),
            ["共同条件/相位数"] = project.Settings.Phases.ToString(CultureInfo.InvariantCulture),
            ["共同条件/公差角点"] = project.Settings.EnumerateTolerance.ToString()
        };
        for (int i = 0; i < project.Panel.Outline.Count; i++)
        {
            rows[$"共同条件/轮廓[{i + 1}]/Xmm"] = N(project.Panel.Outline[i].Xmm);
            rows[$"共同条件/轮廓[{i + 1}]/Ymm"] = N(project.Panel.Outline[i].Ymm);
        }
        foreach (var load in project.Loads)
        {
            var root = "共同条件/载荷/" + load.Id;
            rows[root + "/名称"] = load.Name;
            rows[root + "/法向偏置g"] = N(load.NormalBiasG);
            rows[root + "/法向幅值g"] = N(load.NormalAmplitudeG);
            rows[root + "/切向偏置g"] = N(load.TangentialBiasG);
            rows[root + "/切向幅值g"] = N(load.TangentialAmplitudeG);
            rows[root + "/相差deg"] = N(load.PhaseDegrees);
            rows[root + "/频率标签Hz"] = N(load.FrequencyHz);
            rows[root + "/来源"] = load.Source;
        }
        foreach (var variant in project.Variants)
        {
            var root = "方案/" + variant.Id;
            rows[root + "/名称"] = variant.Name;
            rows[root + "/说明"] = variant.Note;
            foreach (var clip in variant.Clips)
            {
                var key = root + "/卡扣/" + clip.Id;
                rows[key + "/名称"] = clip.Name;
                rows[key + "/Xmm"] = N(clip.Xmm);
                rows[key + "/Ymm"] = N(clip.Ymm);
                rows[key + "/法向刚度N/mm"] = N(clip.NormalStiffness);
                rows[key + "/切向刚度N/mm"] = N(clip.TangentialStiffness);
                rows[key + "/合力限值N"] = N(clip.ForceLimitN);
            }
            foreach (var contact in variant.Contacts)
            {
                var key = root + "/接触点/" + contact.Id;
                rows[key + "/名称"] = contact.Name;
                rows[key + "/材料"] = contact.Material;
                rows[key + "/Xmm"] = N(contact.Xmm);
                rows[key + "/Ymm"] = N(contact.Ymm);
                rows[key + "/法向刚度N/mm"] = N(contact.NormalStiffness);
                rows[key + "/切向刚度N/mm"] = N(contact.TangentialStiffness);
                rows[key + "/间隙mm"] = N(contact.GapMm);
                rows[key + "/公差mm"] = N(contact.ToleranceMm);
                rows[key + "/预紧mm"] = N(contact.PreloadMm);
                rows[key + "/摩擦系数"] = N(contact.Friction);
                rows[key + "/允许压缩mm"] = N(contact.CompressionLimitMm);
            }
        }
        return rows;
    }

    public static DifferenceReport Compare(DesignProject current, DesignProject reference)
    {
        ProjectValidation.Require(current);
        ProjectValidation.Require(reference);
        var left = Flatten(current);
        var right = Flatten(reference);
        var keys = left.Keys.Concat(right.Keys).Distinct().OrderBy(k => k, StringComparer.Ordinal);
        var rows = new List<InputDifference>();
        foreach (var key in keys)
        {
            var a = left.GetValueOrDefault(key, "（不存在）");
            var b = right.GetValueOrDefault(key, "（不存在）");
            if (a != b)
                rows.Add(new(key, a, b));
        }
        return new(ProjectCodec.InputHash(current), ProjectCodec.InputHash(reference), reference.Name,
            !rows.Any(r => r.Path.StartsWith("共同条件/", StringComparison.Ordinal)), rows);
    }
}
