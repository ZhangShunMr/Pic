using System.Globalization;
using TrimContact.Core.Domain;
using TrimContact.Core.Operations;

namespace TrimContact.Core.IO;

public sealed record LayoutImportPreview(DesignProject Proposed, string BaseHash, Guid VariantId,
    int ClipCount, int ContactCount, string Mode, IReadOnlyList<string> Messages);

public static class LayoutCsv
{
    public static readonly string[] Header =
    [
        "kind", "name", "x_mm", "y_mm", "kn_N_per_mm", "kt_N_per_mm", "gap_mm",
        "tolerance_mm", "preload_mm", "mu", "compression_limit_mm", "force_limit_N", "material"
    ];

    public static string Template => string.Join(",", Header) + "\r\n"
        + "Clip,左下卡扣,40,40,100,50,,,,,,80,\r\n"
        + "Clip,右下卡扣,360,40,100,50,,,,,,80,\r\n"
        + "Clip,右上卡扣,360,260,100,50,,,,,,80,\r\n"
        + "Clip,左上卡扣,40,260,100,50,,,,,,80,\r\n"
        + "Contact,中心缓冲块,200,150,100,40,0.1,0.15,0.2,0.3,0.8,,缓冲块（模拟）\r\n";

    public static LayoutImportPreview Preview(DesignProject project, Guid variantId, string csv, bool replace)
    {
        var rows = CsvTable.Read(csv);
        if (!rows[0].Cells.SequenceEqual(Header))
            throw new InvalidDataException("CSV表头、顺序或大小写不匹配，请下载布置CSV模板");
        var clone = ProjectCodec.Clone(project);
        var variant = clone.Variants.SingleOrDefault(v => v.Id == variantId)
            ?? throw new InvalidDataException("先选择目标方案");
        var issues = new List<string>();
        var messages = new List<string>();
        if (replace)
        {
            var oldIds = variant.Clips.Select(c => c.Id).Concat(variant.Contacts.Select(c => c.Id)).ToHashSet();
            DesignOperations.DetachTasks(clone, oldIds);
            variant.Clips.Clear();
            variant.Contacts.Clear();
            messages.Add("替换当前方案全部点位；原关联验证任务保留并解除关联。其他方案不受影响。");
        }
        else
            messages.Add("追加点位；不按名称覆盖或合并，所有导入点位生成新ID。");
        int clipCount = 0;
        int contactCount = 0;
        foreach (var row in rows.Skip(1))
        {
            if (row.Cells.Count != Header.Length)
            {
                issues.Add($"第{row.Line}行：应有{Header.Length}列，实际{row.Cells.Count}列");
                continue;
            }
            var cells = row.Cells;
            var before = issues.Count;
            double Number(int column)
            {
                if (!double.TryParse(cells[column], NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                    || !double.IsFinite(value))
                {
                    issues.Add($"第{row.Line}行/{Header[column]}：需要有限数值，英文小数点，无千位符");
                    return 0;
                }
                return value;
            }
            void Empty(params int[] columns)
            {
                foreach (var column in columns)
                    if (cells[column].Length != 0)
                        issues.Add($"第{row.Line}行/{Header[column]}：该类型不使用此字段，须留空");
            }
            if (string.IsNullOrWhiteSpace(cells[1]))
                issues.Add($"第{row.Line}行/name：名称不能为空");
            if (cells[0] == "Clip")
            {
                var clip = new Clip
                {
                    Name = cells[1],
                    Xmm = Number(2),
                    Ymm = Number(3),
                    NormalStiffness = Number(4),
                    TangentialStiffness = Number(5),
                    ForceLimitN = Number(11)
                };
                Empty(6, 7, 8, 9, 10, 12);
                if (issues.Count == before)
                {
                    variant.Clips.Add(clip);
                    clipCount++;
                }
            }
            else if (cells[0] == "Contact")
            {
                var contact = new ContactPoint
                {
                    Name = cells[1],
                    Xmm = Number(2),
                    Ymm = Number(3),
                    NormalStiffness = Number(4),
                    TangentialStiffness = Number(5),
                    GapMm = Number(6),
                    ToleranceMm = Number(7),
                    PreloadMm = Number(8),
                    Friction = Number(9),
                    CompressionLimitMm = Number(10),
                    Material = cells[12]
                };
                Empty(11);
                if (issues.Count == before)
                {
                    variant.Contacts.Add(contact);
                    contactCount++;
                }
            }
            else
                issues.Add($"第{row.Line}行/kind：只接受Clip或Contact");
        }
        issues.AddRange(ProjectValidation.Check(clone).Select(i => i.Path + "：" + i.Message));
        if (issues.Count > 0)
            throw new InvalidDataException("CSV整批取消：" + string.Join("；", issues.Take(20)));
        messages.Add($"成功预检{clipCount}个卡扣、{contactCount}个接触点；目标方案最终{variant.Clips.Count}卡扣、{variant.Contacts.Count}接触点。");
        messages.Add("CSV不携带试验标定链，导入后须核实项目与载荷来源声明。");
        return new LayoutImportPreview(clone, ProjectCodec.FileHash(System.Text.Json.JsonSerializer.Serialize(project, ProjectCodec.Options)),
            variantId, clipCount, contactCount, replace ? "替换" : "追加", messages);
    }

    public static string Export(DesignVariant variant)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine(string.Join(",", Header));
        string Cell(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        string N(double value) => value.ToString("G17", CultureInfo.InvariantCulture);
        void Add(params string[] cells) => text.AppendLine(string.Join(",", cells.Select(Cell)));
        foreach (var clip in variant.Clips)
        {
            Add("Clip", clip.Name, N(clip.Xmm), N(clip.Ymm), N(clip.NormalStiffness),
                N(clip.TangentialStiffness), "", "", "", "", "", N(clip.ForceLimitN), "");
        }
        foreach (var contact in variant.Contacts)
        {
            Add("Contact", contact.Name, N(contact.Xmm), N(contact.Ymm), N(contact.NormalStiffness),
                N(contact.TangentialStiffness), N(contact.GapMm), N(contact.ToleranceMm),
                N(contact.PreloadMm), N(contact.Friction), N(contact.CompressionLimitMm), "", contact.Material);
        }
        return text.ToString();
    }

    public static DesignProject Apply(DesignProject current, LayoutImportPreview preview)
    {
        var hash = ProjectCodec.FileHash(System.Text.Json.JsonSerializer.Serialize(current, ProjectCodec.Options));
        if (hash != preview.BaseHash)
            throw new InvalidOperationException("预检后项目已有变化，请重新预检，已阻止过期导入");
        ProjectValidation.Require(preview.Proposed);
        return ProjectCodec.Clone(preview.Proposed);
    }
}
