using Blower.Core.Analysis;
using Blower.Core.Domain;
using Blower.Core.IO;

namespace Blower.Core.Operations;

public static class ProjectCommands
{
    public static SpeedPlan CopyPlan(SpeedPlan source, string name)
    {
        return new()
        {
            Name = name,
            Notes = source.Notes,
            Gears = source.Gears.Select(x => new Gear
            {
                Name = x.Name,
                Rpm = x.Rpm,
                Fixed = x.Fixed,
                Notes = x.Notes
            }).ToList()
        };
    }

    public static Project Duplicate(Project source)
    {
        var copy = ProjectCodec.Clone(source);
        copy.Id = Software.Id();
        copy.Name += " · 副本";
        copy.Created = DateTimeOffset.UtcNow;
        copy.Modified = copy.Created;
        copy.Archived = false;
        copy.Revision = 0;
        copy.Audit = [new() { Action = "复制项目", Detail = source.Id + " / 修订" + source.Revision }];
        return copy;
    }

    public static void RemoveObject(Project project, string kind, string id)
    {
        var related = project.Checks.Where(x => kind switch
        {
            "mode" => x.ModeId == id,
            "order" => x.OrderId == id,
            "gear" => x.GearId == id,
            "plan" => x.PlanId == id,
            _ => false
        }).ToList();
        if (related.Count > 0)
        {
            throw new InvalidOperationException($"仍有{related.Count}条验证记录引用此对象；请先移除相关验证记录");
        }
        switch (kind)
        {
            case "mode":
                project.Modes.RemoveAll(x => x.Id == id);
                break;
            case "order":
                project.Orders.RemoveAll(x => x.Id == id);
                break;
            case "plan":
                if (project.Plans.Single(x => x.Id == id).Locked)
                {
                    throw new InvalidOperationException("先解除方案锁定");
                }
                project.Plans.RemoveAll(x => x.Id == id);
                break;
            case "gear":
                var plan = project.Plans.Single(x => x.Gears.Any(g => g.Id == id));
                if (plan.Locked)
                {
                    throw new InvalidOperationException("先解除方案锁定");
                }
                plan.Gears.RemoveAll(x => x.Id == id);
                break;
            case "check":
                project.Checks.RemoveAll(x => x.Id == id);
                break;
            default:
                throw new InvalidOperationException("此对象不可删除");
        }
    }

    public static SpeedPlan ApplyScan(Project project, ScanResult scan)
    {
        if (!scan.Feasible || scan.InputHash != ProjectCodec.InputHash(project))
        {
            throw new InvalidOperationException("扫描无可行解或输入已变化，请重新扫描");
        }
        var original = project.Plans.Single(x => x.Id == scan.PlanId);
        var candidate = CopyPlan(original, original.Name + " · 扫描候选");
        for (var i = 0; i < candidate.Gears.Count; i++)
        {
            candidate.Gears[i].Rpm = scan.Choices[i].CandidateRpm;
        }
        candidate.Notes = "离散扫描候选；目标=接近组合数+偏移权重×归一化偏移；风量及响应待验证";
        project.Plans.Add(candidate);
        return candidate;
    }

    public static int GenerateChecks(Project project, AnalysisResult result, string planId)
    {
        if (result.InputHash != ProjectCodec.InputHash(project))
        {
            throw new InvalidOperationException("计算已过期；先重新计算");
        }
        var count = 0;
        foreach (var contact in result.Contacts.Where(x => x.PlanId == planId && x.Near))
        {
            if (project.Checks.Any(x => x.Key == contact.Key))
            {
                continue;
            }
            project.Checks.Add(new()
            {
                Key = contact.Key,
                PlanId = contact.PlanId,
                GearId = contact.GearId,
                ModeId = contact.ModeId,
                OrderId = contact.OrderId,
                Title = $"{contact.Gear} / {contact.Order} / {contact.Mode}",
                Method = $"在{contact.Rpm:0.##}rpm附近复核转速与阶次；测量壳体/风道响应和车内声音；记录温度、安装、测点、仪器、频率分辨率及相干性。候选频率{contact.FrequencyHz:0.##}Hz。核对风量与效率。"
            });
            count++;
        }
        return count;
    }

    public static void MarkChecksForRecheck(Project project)
    {
        foreach (var task in project.Checks.Where(x => x.State == CheckState.Completed))
        {
            task.State = CheckState.Recheck;
            task.Updated = DateTimeOffset.UtcNow;
        }
    }
}
