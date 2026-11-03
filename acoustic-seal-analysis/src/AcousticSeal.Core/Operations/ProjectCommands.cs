using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;

namespace AcousticSeal.Core.Operations;

public static class ProjectCommands
{
    public static void SaveLocation(ProjectSession session, SealLocation location)
    {
        session.Apply("应用封堵位置", location.Id.ToString(), project => Upsert(project.Locations, location, item => item.Id));
    }

    public static void SaveCondition(ProjectSession session, MeasurementCondition condition)
    {
        session.Apply("应用工况", condition.Id.ToString(), project => Upsert(project.Conditions, condition, item => item.Id));
    }

    public static void SaveTrial(ProjectSession session, AcousticTrial trial)
    {
        session.Apply("应用试验", trial.Id.ToString(), project => Upsert(project.Trials, trial, item => item.Id));
    }

    public static void SavePair(ProjectSession session, TrialPair pair)
    {
        session.Apply("应用显式配对", pair.Id.ToString(), project => Upsert(project.Pairs, pair, item => item.Id));
    }

    public static void DeleteLocation(ProjectSession session, Guid id)
    {
        session.Apply("删除封堵位置", id.ToString(), project =>
        {
            var related = project.Trials.Where(trial => trial.LocationIds.Contains(id)).Select(trial => trial.Name).ToList();
            EnsureNoReferences(related);
            EnsureRemoved(project.Locations.RemoveAll(item => item.Id == id));
        });
    }

    public static void DeleteCondition(ProjectSession session, Guid id)
    {
        session.Apply("删除工况", id.ToString(), project =>
        {
            var related = project.Trials.Where(trial => trial.ConditionId == id).Select(trial => trial.Name).ToList();
            EnsureNoReferences(related);
            EnsureRemoved(project.Conditions.RemoveAll(item => item.Id == id));
        });
    }

    public static void DeleteTrial(ProjectSession session, Guid id)
    {
        session.Apply("删除试验", id.ToString(), project =>
        {
            var related = project.Pairs.Where(pair => pair.BaselineId == id || pair.TargetId == id)
                .Select(pair => pair.Id.ToString()).ToList();
            EnsureNoReferences(related);
            EnsureRemoved(project.Trials.RemoveAll(item => item.Id == id));
        });
    }

    public static void DeletePair(ProjectSession session, Guid id)
    {
        session.Apply("删除配对", id.ToString(), project => EnsureRemoved(project.Pairs.RemoveAll(item => item.Id == id)));
    }

    public static Guid DuplicateTrial(ProjectSession session, Guid id)
    {
        var newId = Guid.NewGuid();
        session.Apply("复制试验结构", id.ToString(), project =>
        {
            var original = project.Trials.FirstOrDefault(trial => trial.Id == id)
                ?? throw new DomainValidationException("trial", "试验不存在");
            var copy = ProjectCodec.Clone(original);
            copy.Id = newId;
            copy.Name = (copy.Name.Length > 140 ? copy.Name[..140] : copy.Name) + " 副本（待录入）";
            copy.Bands = [];
            copy.RepeatGroup = "";
            copy.Timestamp = DateTimeOffset.UtcNow;
            copy.Notes = "复制结构未复制测量与配对，时间和来源须复核。";
            project.Trials.Add(copy);
        });
        return newId;
    }

    public static void SetArchived(ProjectSession session, bool archived)
    {
        session.Apply(archived ? "归档项目" : "解除归档", session.Project.Id.ToString(),
            project => project.Status = archived ? ProjectStatus.Archived : ProjectStatus.Active, allowArchived: true);
    }

    public static void SaveSettings(ProjectSession session, AnalysisSettings settings)
    {
        session.Apply("应用参数并重新计算全部历史输入", session.Project.Id.ToString(),
            project => project.Settings = ProjectCodec.Clone(settings));
    }

    private static void Upsert<T>(List<T> items, T value, Func<T, Guid> key)
    {
        var index = items.FindIndex(item => key(item) == key(value));
        if (index < 0)
        {
            items.Add(ProjectCodec.Clone(value));
        }
        else
        {
            items[index] = ProjectCodec.Clone(value);
        }
    }

    private static void EnsureNoReferences(List<string> related)
    {
        if (related.Count > 0)
        {
            throw new DomainValidationException("references", "仍被引用，请先处理关联对象：" + string.Join("、", related.Take(10)));
        }
    }

    private static void EnsureRemoved(int count)
    {
        if (count == 0)
        {
            throw new DomainValidationException("id", "对象已不存在，请重新选择");
        }
    }
}
