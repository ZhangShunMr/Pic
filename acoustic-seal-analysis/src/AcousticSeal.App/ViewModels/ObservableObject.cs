using System.ComponentModel;
using System.Runtime.CompilerServices;
using AcousticSeal.Core.Domain;

namespace AcousticSeal.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;

    protected void Notify([CallerMemberName] string? property = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        Changed?.Invoke();
    }
}

public abstract class EditorViewModel : ObservableObject
{
    public bool IsDirty { get; private set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool IsExisting { get; protected set; }
    public string IdentityLabel => IsExisting ? Id.ToString() : "应用后建立对象";

    public void MarkDirty()
    {
        IsDirty = true;
        Notify(nameof(IsDirty));
    }

    public void MarkClean()
    {
        IsDirty = false;
        Notify(nameof(IsDirty));
    }

    protected static string Text(string? value)
    {
        return value?.Trim() ?? "";
    }
}

public sealed record UserNotice(string Text, bool Error = false);
public sealed record WorkspacePage(string Key, string Number, string Title, string Description);

public static class Navigation
{
    public static readonly IReadOnlyList<WorkspacePage> Pages =
    [
        new("project", "01", "项目工作台", "定义试验目标、管理项目身份与本机项目库"),
        new("locations", "02", "封堵位置与工艺", "建立可复现的位置、材料、覆盖及恢复步骤"),
        new("conditions", "03", "工况与测量口径", "核对车辆、测点、激励、计权、标定和数值工况"),
        new("trials", "04", "试验与频带", "组织四类试验并逐频带记录数值、缺失与来源"),
        new("import", "05", "CSV批量导入", "预检、核对覆盖范围，再整体应用"),
        new("pairs", "06", "显式试验配对", "为每条比较明确选择基线、目标与用途"),
        new("analysis", "07", "比较与统计", "查看测得声级差、矩阵和重复配对统计"),
        new("quality", "08", "质量复核", "核查重复性、漂移、恢复、覆盖与可执行修复建议"),
        new("history", "09", "审计与复核快照", "记录本次判断、参数、输入指纹和操作轨迹"),
        new("revision", "10", "项目版本核对", "按GUID和频率比较两份项目内容，保留输入身份"),
        new("delivery", "11", "保存与成果", "实际保存、备份、重开和可追溯报告输出")
    ];

    public static WorkspacePage Get(string key)
    {
        return Pages.FirstOrDefault(page => page.Key == key) ?? Pages[0];
    }
}
