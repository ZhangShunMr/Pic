using TrimContact.Core.Domain;
using TrimContact.Core.Operations;

namespace TrimContact.App.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private void DraftEdit(Action action)
    {
        Execute(() =>
        {
            if (Project.Archived)
                throw new InvalidOperationException("归档项目只读，请先复制项目");
            action();
            NormalizeSelection();
            Message("工作编辑已变化，点击应用编辑、计算或保存完成校验。");
        });
    }

    public void AddVariant() => DraftEdit(() =>
    {
        if (Draft.Variants.Count >= 8)
            throw new InvalidOperationException("最多8个方案");
        var variant = new DesignVariant { Name = "方案 " + (Draft.Variants.Count + 1) };
        Draft.Variants.Add(variant);
        VariantId = variant.Id;
    });

    public void DuplicateVariant() => DraftEdit(() =>
    {
        if (Variant is null)
            throw new InvalidOperationException("先选择方案");
        if (Draft.Variants.Count >= 8)
            throw new InvalidOperationException("最多8个方案");
        var name = Variant.Name.Length > 95 ? Variant.Name[..95] : Variant.Name;
        var variant = DesignOperations.CopyVariant(Variant, name + " 副本");
        Draft.Variants.Add(variant);
        VariantId = variant.Id;
    });

    public void DeleteVariant() => Request("删除当前方案？关联验证任务将保留并解除点位关联。", () =>
    {
        if (VariantId is null)
            return;
        DraftEdit(() => DesignOperations.RemoveVariant(Draft, VariantId.Value));
    });

    public void AddClip() => DraftEdit(() =>
    {
        if (Variant is null)
            throw new InvalidOperationException("先建立方案");
        if (Variant.Clips.Count >= 12)
            throw new InvalidOperationException("每方案最多12个卡扣");
        var clip = new Clip
        {
            Name = "卡扣 " + (Variant.Clips.Count + 1),
            Xmm = Draft.Panel.CenterXmm,
            Ymm = Draft.Panel.CenterYmm
        };
        Variant.Clips.Add(clip);
        ClipId = clip.Id;
    });

    public void AddContact() => DraftEdit(() =>
    {
        if (Variant is null)
            throw new InvalidOperationException("先建立方案");
        if (Variant.Contacts.Count >= 6)
            throw new InvalidOperationException("每方案最多6个接触点");
        var contact = new ContactPoint
        {
            Name = "接触点 " + (Variant.Contacts.Count + 1),
            Xmm = Draft.Panel.CenterXmm,
            Ymm = Draft.Panel.CenterYmm
        };
        Variant.Contacts.Add(contact);
        ContactId = contact.Id;
    });

    public void DeleteClip() => Request("删除选中卡扣？关联验证任务会解除点位关联。", () => DraftEdit(() =>
    {
        if (Clip is null || Variant is null)
            return;
        DesignOperations.DetachTasks(Draft, [Clip.Id]);
        Variant.Clips.Remove(Clip);
    }));

    public void DeleteContact() => Request("删除选中接触点？关联验证任务会解除点位关联。", () => DraftEdit(() =>
    {
        if (Contact is null || Variant is null)
            return;
        DesignOperations.DetachTasks(Draft, [Contact.Id]);
        Variant.Contacts.Remove(Contact);
    }));

    public void AddVertex() => DraftEdit(() =>
    {
        if (Draft.Panel.Outline.Count >= 24)
            throw new InvalidOperationException("轮廓最多24个顶点");
        Draft.Panel.Outline.Add(new Vertex());
    });

    public void Rectangle() => Request("以400×300mm矩形替换轮廓？点位和质心仍需在轮廓内。", () => DraftEdit(() =>
    {
        Draft.Panel.Outline = [new() { Xmm = 0, Ymm = 0 }, new() { Xmm = 400, Ymm = 0 },
            new() { Xmm = 400, Ymm = 300 }, new() { Xmm = 0, Ymm = 300 }];
        Draft.Panel.CenterXmm = 200;
        Draft.Panel.CenterYmm = 150;
    }));

    public void DeleteVertex(Vertex vertex) => Request("删除轮廓顶点？请检查相邻边和自交。", () =>
        DraftEdit(() => Draft.Panel.Outline.Remove(vertex)));

    public void AddLoad() => DraftEdit(() =>
    {
        if (Draft.Loads.Count >= 6)
            throw new InvalidOperationException("最多6个共用载荷");
        var load = new LoadCase { Name = "载荷 " + (Draft.Loads.Count + 1) };
        Draft.Loads.Add(load);
        LoadId = load.Id;
    });

    public void DeleteLoad() => Request("删除选中载荷？下次计算将重新生成所有结果。", () => DraftEdit(() =>
    {
        if (Load is not null)
            Draft.Loads.Remove(Load);
    }));

    public void AddTask() => DraftEdit(() =>
    {
        if (Draft.Tasks.Count >= 200)
            throw new InvalidOperationException("最多200项验证任务");
        var task = new VerificationTask();
        Draft.Tasks.Add(task);
        TaskId = task.Id;
    });

    public void DeleteTask() => Request("删除选中验证任务及其证据记录？", () => DraftEdit(() =>
    {
        if (Task is not null)
            Draft.Tasks.Remove(Task);
    }));
}
