using TrimContact.Core.Analysis;
using TrimContact.Core.Domain;
using TrimContact.Core.IO;

namespace TrimContact.Core.Operations;

public static class DesignOperations
{
    public static DesignVariant CopyVariant(DesignVariant source, string name)
    {
        var project = new DesignProject { Variants = [source] };
        var result = ProjectCodec.Clone(project).Variants[0];
        result.Id = Guid.NewGuid();
        result.Name = name;
        foreach (var clip in result.Clips)
            clip.Id = Guid.NewGuid();
        foreach (var contact in result.Contacts)
            contact.Id = Guid.NewGuid();
        return result;
    }

    public static void RemoveVariant(DesignProject project, Guid id)
    {
        var variant = project.Variants.Single(v => v.Id == id);
        var ids = variant.Clips.Select(c => c.Id).Concat(variant.Contacts.Select(c => c.Id)).ToHashSet();
        DetachTasks(project, ids);
        project.Variants.Remove(variant);
    }

    public static void DetachTasks(DesignProject project, HashSet<Guid> ids)
    {
        foreach (var task in project.Tasks.Where(t => t.PointId.HasValue && ids.Contains(t.PointId.Value)))
        {
            task.PointId = null;
            task.Title = "[原点位已删除] " + task.Title;
            if (task.Title.Length > 200)
                task.Title = task.Title[..200];
        }
    }

    public static int GenerateTasks(DesignProject project, AnalysisReport report)
    {
        if (report.InputHash != ProjectCodec.InputHash(project))
            throw new InvalidOperationException("结果已失效，请重新计算后生成验证任务");
        var added = 0;
        void Add(Guid? point, string title, string method, string acceptance)
        {
            if (project.Tasks.Any(t => t.PointId == point && t.Title == title))
                return;
            if (project.Tasks.Count >= 200)
                throw new InvalidDataException("任务超过200项，请整理已有任务");
            project.Tasks.Add(new VerificationTask
            {
                PointId = point,
                Title = title,
                Method = method,
                Acceptance = acceptance
            });
            added++;
        }
        Add(null, "参数识别与模型复核", "测量装配间隙、公差、卡扣刚度、接触压缩曲线和材料摩擦；记录温度与加载条件。",
            "由项目负责人填写经试验认可的参数范围；本软件不提供行业合格限值。");
        foreach (var variant in report.Variants)
        {
            foreach (var c in variant.Contacts)
            {
                if (c.MinCompressionMm <= Product.ContactEpsilon)
                    Add(c.PointId, c.Name + "接触保持核查", "按控制工况装配上下限样件，测量间隙和分离；控制工况：" + c.CompressionCase,
                        "记录允许分离条件及实测证据，结合道路或台架人工听辨确认。");
                if (c.MaxSlipMm > Product.ContactEpsilon)
                    Add(c.PointId, c.Name + "摩擦滑移核查", "对材料副进行切向位移与摩擦试验；控制工况：" + c.SlipCase,
                        "比较材料磨耗与实测异响表现，判据由项目约定，不以模拟滑移直接判异响。");
                if (c.MaxUtilization > 1)
                    Add(c.PointId, c.Name + "压缩行程核查", "核实缓冲块可压缩行程与装配压缩量：" + c.GoverningCase,
                        "实测压缩量不超过经确认的材料允许值。");
            }
            foreach (var c in variant.Clips.Where(c => c.MaxUtilization > 1))
                Add(c.PointId, c.Name + "卡扣合力核查", "复核卡扣合力与方向；控制工况：" + c.GoverningCase,
                    "与供应商保持力和耐久试验结果比较，不能将软件初值当作允许载荷。");
        }
        return added;
    }
}
