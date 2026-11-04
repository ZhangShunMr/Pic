using TrimContact.Core.Domain;
using TrimContact.Core.IO;

namespace TrimContact.Core.Operations;

public sealed class BatchLayoutRequest
{
    public string Target { get; set; } = "接触点";
    public double ShiftXmm { get; set; }
    public double ShiftYmm { get; set; }
    public double NormalStiffnessFactor { get; set; } = 1;
    public double TangentialStiffnessFactor { get; set; } = 1;
    public double GapDeltaMm { get; set; }
    public double PreloadDeltaMm { get; set; }
}

public sealed record BatchChange(string Point, string Field, string Unit, double Before, double After);

public sealed record BatchPreview(DesignProject Proposed, string BaseState, string RequestState,
    string VariantName, IReadOnlyList<BatchChange> Changes);

public static class BatchLayout
{
    public static string State<T>(T value) => System.Text.Json.JsonSerializer.Serialize(value, ProjectCodec.Options);

    public static BatchPreview Preview(DesignProject project, Guid variantId, BatchLayoutRequest request)
    {
        if (project.Archived)
            throw new InvalidOperationException("归档项目不能批量修改");
        if (request.Target is not ("接触点" or "卡扣" or "全部点位"))
            throw new InvalidDataException("未知批量修改范围");
        double[] values = [request.ShiftXmm, request.ShiftYmm, request.NormalStiffnessFactor,
            request.TangentialStiffnessFactor, request.GapDeltaMm, request.PreloadDeltaMm];
        if (values.Any(v => !double.IsFinite(v)))
            throw new InvalidDataException("批量参数须为有限数值");
        if (Math.Abs(request.ShiftXmm) > 1000 || Math.Abs(request.ShiftYmm) > 1000)
            throw new InvalidDataException("单次点位平移不超过±1000mm");
        if (request.NormalStiffnessFactor is <= 0 or > 100 || request.TangentialStiffnessFactor is <= 0 or > 100)
            throw new InvalidDataException("刚度缩放系数须大于0且不超过100");
        if (Math.Abs(request.GapDeltaMm) > 20 || Math.Abs(request.PreloadDeltaMm) > 20)
            throw new InvalidDataException("单次间隙和预紧增量不超过±20mm");
        if (request.Target == "卡扣" && (request.GapDeltaMm != 0 || request.PreloadDeltaMm != 0))
            throw new InvalidDataException("卡扣不使用间隙和预紧量，增量须为0");
        var clone = ProjectCodec.Clone(project);
        var variant = clone.Variants.SingleOrDefault(v => v.Id == variantId)
            ?? throw new InvalidDataException("目标方案不存在");
        var changes = new List<BatchChange>();
        double Record(string name, string field, string unit, double before, double after)
        {
            if (before != after)
                changes.Add(new(name, field, unit, before, after));
            return after;
        }
        if (request.Target is "卡扣" or "全部点位")
        {
            foreach (var clip in variant.Clips)
            {
                clip.Xmm = Record(clip.Name, "X", "mm", clip.Xmm, clip.Xmm + request.ShiftXmm);
                clip.Ymm = Record(clip.Name, "Y", "mm", clip.Ymm, clip.Ymm + request.ShiftYmm);
                clip.NormalStiffness = Record(clip.Name, "法向刚度", "N/mm", clip.NormalStiffness,
                    clip.NormalStiffness * request.NormalStiffnessFactor);
                clip.TangentialStiffness = Record(clip.Name, "切向刚度", "N/mm", clip.TangentialStiffness,
                    clip.TangentialStiffness * request.TangentialStiffnessFactor);
            }
        }
        if (request.Target is "接触点" or "全部点位")
        {
            foreach (var contact in variant.Contacts)
            {
                contact.Xmm = Record(contact.Name, "X", "mm", contact.Xmm, contact.Xmm + request.ShiftXmm);
                contact.Ymm = Record(contact.Name, "Y", "mm", contact.Ymm, contact.Ymm + request.ShiftYmm);
                contact.NormalStiffness = Record(contact.Name, "法向刚度", "N/mm", contact.NormalStiffness,
                    contact.NormalStiffness * request.NormalStiffnessFactor);
                contact.TangentialStiffness = Record(contact.Name, "切向刚度", "N/mm", contact.TangentialStiffness,
                    contact.TangentialStiffness * request.TangentialStiffnessFactor);
                contact.GapMm = Record(contact.Name, "名义间隙", "mm", contact.GapMm,
                    contact.GapMm + request.GapDeltaMm);
                contact.PreloadMm = Record(contact.Name, "预紧量", "mm", contact.PreloadMm,
                    contact.PreloadMm + request.PreloadDeltaMm);
            }
        }
        if (changes.Count == 0)
            throw new InvalidDataException("没有参数发生变化，请选择有效点位并输入非默认增量或缩放");
        ProjectValidation.Require(clone);
        return new(clone, State(project), State(request), variant.Name, changes);
    }

    public static DesignProject Apply(DesignProject project, BatchLayoutRequest request, BatchPreview preview)
    {
        if (State(project) != preview.BaseState || State(request) != preview.RequestState)
            throw new InvalidOperationException("批量预览后输入已变化，请重新预览");
        ProjectValidation.Require(preview.Proposed);
        return ProjectCodec.Clone(preview.Proposed);
    }
}
