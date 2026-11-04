using Avas.Core.Analysis;
using Avas.Core.IO;

namespace Avas.App.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public List<ReviewFinding> Review => ReviewEngine.Inspect(Project);
    public TransectRequest TransectRequest { get; set; } = new();
    public TransectResult? Transect { get; private set; }

    public async Task RunTransectAsync()
    {
        if (Busy)
            return;
        Busy = true;
        Status = "正在计算指定方向剖面…";
        Notify();
        await Task.Yield();
        Execute(() =>
        {
            RequireApplied();
            Transect = TransectEngine.Run(Project, TransectRequest);
            Status = $"剖面完成：{Transect.Points.Count}个采样点，不改变项目观察点。";
        });
        Busy = false;
        Notify();
    }

    public void ExportTransect()
    {
        Execute(() =>
        {
            RequireApplied();
            if (Transect is null || Transect.InputHash != ProjectCodec.InputHash(Project))
                throw new ArgumentException("剖面不存在或已过期，请重新运行剖面");
            LastFileName = SafeName(Project.Name) + "-方向剖面.csv";
            LastDownload = vault.Add(TransectEngine.Csv(Transect), "text/csv;charset=utf-8", LastFileName);
            Status = "方向剖面CSV已生成，点击底部链接下载。";
        });
    }
}
