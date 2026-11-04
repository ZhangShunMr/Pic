using Avas.Core.Analysis;
using Avas.Core.Domain;
using Avas.Core.IO;

namespace Avas.App.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public void AddSpeaker()
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            var candidate = ProjectCodec.Clone(Project);
            if (candidate.Speakers.Count >= 8)
                throw new ArgumentException("最多8个扬声器");
            var source = new Speaker { Name = $"扬声器{candidate.Speakers.Count + 1}" };
            candidate.Speakers.Add(source);
            SelectedSpeakerId = source.Id;
            Commit(candidate, "新增扬声器 " + source.Name);
            Status = "已添加扬声器，请核对位置、频带及来源。";
        });
    }

    public void DuplicateSpeaker()
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            var candidate = ProjectCodec.Clone(Project);
            if (candidate.Speakers.Count >= 8)
                throw new ArgumentException("最多8个扬声器");
            var source = ProjectCodec.Clone(candidate.Speakers.Single(x => x.Id == SelectedSpeakerId));
            source.Id = Guid.NewGuid();
            source.Name = source.Name.Length < 96 ? source.Name + "副本" : "扬声器副本";
            candidate.Speakers.Add(source);
            SelectedSpeakerId = source.Id;
            Commit(candidate, "复制扬声器 " + source.Name);
            Status = "扬声器已复制，位置暂时相同；请调整后重新计算。";
        });
    }

    public void RemoveSpeaker()
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            var candidate = ProjectCodec.Clone(Project);
            var source = candidate.Speakers.Single(x => x.Id == SelectedSpeakerId);
            candidate.Speakers.Remove(source);
            Commit(candidate, "删除扬声器 " + source.Name);
            Status = "已移除当前扬声器；历史快照仍保留原始输入。";
        });
    }

    public void AddListener()
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            var candidate = ProjectCodec.Clone(Project);
            if (candidate.Listeners.Count >= 100)
                throw new ArgumentException("最多100个观察点");
            var point = new Listener { Name = $"观察点{candidate.Listeners.Count + 1}" };
            candidate.Listeners.Add(point);
            SelectedListenerId = point.Id;
            Commit(candidate, "新增观察点 " + point.Name);
            Status = "已添加观察点。";
        });
    }

    public void AddRing(double radius, int count)
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            if (!double.IsFinite(radius) || radius is < 1 or > 50 || count is < 4 or > 36)
                throw new ArgumentException("环形观察点半径1至50m，数量4至36");
            if (Project.Listeners.Count + count > 100)
                throw new ArgumentException("添加后观察点数量超过100");
            var candidate = ProjectCodec.Clone(Project);
            for (var i = 0; i < count; i++)
            {
                var angle = 2 * Math.PI * i / count;
                candidate.Listeners.Add(new Listener
                {
                    Name = $"环形{radius:g}m-{i + 1}",
                    X = Math.Round(radius * Math.Cos(angle), 5),
                    Y = Math.Round(radius * Math.Sin(angle), 5),
                    Note = "以车辆中心为圆心等角生成；非标准测点布置"
                });
            }
            Commit(candidate, $"生成{count}个半径{radius}m环形观察点");
            Status = "环形观察点已生成；这是自定义几何点集，不是标准测点。";
        });
    }

    public void RemoveListener()
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            var candidate = ProjectCodec.Clone(Project);
            var point = candidate.Listeners.Single(x => x.Id == SelectedListenerId);
            candidate.Listeners.Remove(point);
            Commit(candidate, "删除观察点 " + point.Name);
            Status = "观察点已删除；历史快照独立保留。";
        });
    }

    public async Task CalculateAsync()
    {
        if (Busy)
            return;
        Busy = true;
        Status = "正在计算离散网格与观察点…";
        Notify();
        await Task.Yield();
        Execute(() =>
        {
            RequireApplied();
            Result = FieldEngine.Calculate(Project);
            ClearAudio();
            ClearDownload();
            Status = $"计算完成：{Result.Grid.Count}个有效网格，工程目标覆盖率{Result.Coverage:F2}%。";
        });
        Busy = false;
        Notify();
    }

    public void CaptureSnapshot()
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            if (Stale)
                throw new ArgumentException("请先计算当前输入，再保存方案快照");
            if (Project.Snapshots.Count >= 20)
                throw new ArgumentException("最多20个快照，请删除不需要的快照");
            var candidate = ProjectCodec.Clone(Project);
            var snapshot = ScenarioEngine.Capture(Project, SnapshotName);
            candidate.Snapshots.Add(snapshot);
            Commit(candidate, "保存方案快照 " + snapshot.Name);
            if (string.IsNullOrEmpty(BaselineId))
                BaselineId = snapshot.Id.ToString();
            else
                TargetId = snapshot.Id.ToString();
            Status = "已保存独立方案快照；快照随项目文件内嵌保存。";
        });
    }

    public void RestoreSnapshot(Guid id)
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            var snapshot = Project.Snapshots.Single(x => x.Id == id);
            var restored = ScenarioEngine.Restore(snapshot);
            var candidate = ProjectCodec.Clone(Project);
            candidate.Settings = restored.Settings;
            candidate.Speakers = restored.Speakers;
            candidate.Listeners = restored.Listeners;
            Commit(candidate, "恢复快照输入 " + snapshot.Name);
            Status = "已恢复快照的布局、频谱、背景与观察点，请重新计算。";
        });
    }

    public void DeleteSnapshot(Guid id)
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            var candidate = ProjectCodec.Clone(Project);
            var snapshot = candidate.Snapshots.Single(x => x.Id == id);
            candidate.Snapshots.Remove(snapshot);
            Commit(candidate, "删除快照 " + snapshot.Name);
            if (BaselineId == id.ToString())
                BaselineId = "";
            if (TargetId == id.ToString())
                TargetId = "";
            Status = "快照已删除。";
        });
    }

    public void Compare()
    {
        Execute(() =>
        {
            RequireApplied();
            if (!Guid.TryParse(BaselineId, out var baselineId) || !Guid.TryParse(TargetId, out var targetId))
                throw new ArgumentException("请选择基线与目标两个快照");
            if (baselineId == targetId)
                throw new ArgumentException("基线与目标应为两个不同快照");
            Comparison = ScenarioEngine.Compare(Project.Snapshots.Single(x => x.Id == baselineId), Project.Snapshots.Single(x => x.Id == targetId));
            Status = Comparison.Comparable ? "比较完成；差值=目标减基线，覆盖差为百分点。" : "比较已阻断，请查看不一致条件。";
        });
    }

    public async Task RunScanAsync()
    {
        if (Busy)
            return;
        Busy = true;
        Status = "正在逐项扫描，原项目参数保持原值…";
        Notify();
        await Task.Yield();
        Execute(() =>
        {
            RequireApplied();
            Scan = ScanEngine.Run(Project, ScanRequest);
            Status = $"扫描完成，共{Scan.Rows.Count}组。点击某行“应用该值”会修改项目并使结果过期。";
        });
        Busy = false;
        Notify();
    }

    public void ApplyScan(double value)
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            if (Scan is null || Scan.InputHash != ProjectCodec.InputHash(Project))
                throw new ArgumentException("扫描结果已过期，请重新扫描");
            var candidate = ProjectCodec.Clone(Project);
            var source = candidate.Speakers.Single(x => x.Id == Scan.SpeakerId);
            ScanEngine.Apply(source, Scan.Parameter, value);
            Commit(candidate, $"应用扫描取值 {ScanEngine.Label(Scan.Parameter)}={value}");
            Status = "已应用扫描值，请重新计算。";
        });
    }
}
