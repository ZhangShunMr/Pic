using System.Text;
using Avas.Core.IO;

namespace Avas.App.ViewModels;

public sealed partial class WorkspaceViewModel
{
    public string ListenerText { get; set; } = ListenerCsv.Template();
    public ListenerPreview? ListenerPreview { get; private set; }
    public bool ReplaceListeners { get; set; }
    private string? listenerPreviewText;

    public void PreviewListeners()
    {
        Execute(() =>
        {
            RequireApplied();
            ListenerPreview = ListenerCsv.Parse(ListenerText);
            listenerPreviewText = ListenerText;
            Status = ListenerPreview.Valid ? "观察点预检通过，尚未写入；选择追加或替换后应用。" : "观察点预检失败，旧点集保留。";
        });
    }

    public void ApplyListeners()
    {
        Execute(() =>
        {
            RequireApplied();
            RequireEditable();
            if (ListenerPreview is null || !ListenerPreview.Valid || listenerPreviewText != ListenerText)
                throw new ArgumentException("观察点预检无效或文本已更改，请重新预检");
            var candidate = ProjectCodec.Clone(Project);
            if (ReplaceListeners)
                candidate.Listeners.Clear();
            foreach (var point in ListenerPreview.Points)
            {
                var copy = ProjectCodec.Clone(point);
                copy.Id = Guid.NewGuid();
                candidate.Listeners.Add(copy);
            }
            var importedCount = ListenerPreview.Points.Count;
            Commit(candidate, (ReplaceListeners ? "替换" : "追加") + "观察点CSV，共" + importedCount + "点");
            ListenerPreview = null;
            Status = "观察点批量导入完成。新点使用独立标识，旧快照仍保留旧标识；比较时请注意对应条件。";
        });
    }

    public void ExportListeners(bool template)
    {
        Execute(() =>
        {
            RequireApplied();
            var bytes = template ? Encoding.UTF8.GetBytes("\uFEFF" + ListenerCsv.Template()) : ListenerCsv.Write(Project.Listeners);
            LastFileName = template ? "观察点模板.csv" : SafeName(Project.Name) + "-观察点输入.csv";
            LastDownload = vault.Add(bytes, "text/csv;charset=utf-8", LastFileName);
            Status = "观察点交换文件已生成。输入交换不保留GUID与多行备注；完整保存应使用项目文件。";
        });
    }
}
