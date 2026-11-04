using Avas.Core.Analysis;
using Avas.Core.IO;

namespace Avas.Workspace;

public sealed partial class WorkspaceViewModel
{
    public List<ReviewFinding> Review => ReviewEngine.Inspect(Project);
    public TransectRequest TransectRequest { get; set; } = new();
    public TransectResult? Transect { get; private set; }

    public async Task RunTransectAsync()
    {
        RequireApplied();
        var input = ProjectCodec.Clone(Project);
        var request = ProjectCodec.Clone(TransectRequest);
        await RunWorkAsync(() => TransectEngine.Run(input, request), result =>
        {
            Transect = result;
            Status = $"剖面完成：{result.Points.Count}个采样点，不改变项目观察点。";
        });
    }

    public void ExportTransect()
    {
        Execute(() =>
        {
            RequireApplied();
            if (Transect is null || Transect.InputHash != ProjectCodec.InputHash(Project))
                throw new ArgumentException("剖面不存在或已过期，请重新运行剖面");
            LastFileName = SafeName(Project.Name) + "-方向剖面.csv";
            LastDownload = files.WriteExport(TransectEngine.Csv(Transect), LastFileName);
            Status = "方向剖面CSV已生成，保存在所选文件路径。";
        });
    }
}
