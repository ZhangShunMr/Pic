using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Blower.Desktop.Services;
using Blower.Desktop.ViewModels;

namespace Blower.Desktop;

public partial class MainWindow : Window
{
    private readonly WorkspaceViewModel workspace;
    public MainWindow()
    {
        InitializeComponent();
        workspace = new WorkspaceViewModel(new DesktopDialogs());
        DataContext = workspace;
    }
    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = !workspace.CanClose();
    }
    private void GenerateColumn(object? sender, DataGridAutoGeneratingColumnEventArgs e)
    {
        if (Hidden.Contains(e.PropertyName))
        {
            e.Cancel = true;
            return;
        }
        e.Column.Header = Headers.GetValueOrDefault(e.PropertyName, e.PropertyName);
        e.Column.MinWidth = 90;
        e.Column.MaxWidth = e.PropertyType == typeof(string) ? 360 : 180;
        if (e.PropertyType == typeof(double) || e.PropertyType == typeof(double?))
        {
            if (e.Column is DataGridTextColumn text)
            {
                text.Binding = new Binding(e.PropertyName) { StringFormat = "0.###" };
            }
        }
        if (e.PropertyType == typeof(DateTimeOffset) && e.Column is DataGridTextColumn date)
        {
            date.Binding = new Binding(e.PropertyName) { StringFormat = "yyyy-MM-dd HH:mm:ss zzz" };
        }
    }
    private static readonly HashSet<string> Hidden =
    [
        "Id", "PlanId", "GearId", "OrderId", "ModeId", "Key", "Gears", "InputHash"
    ];
    private static readonly Dictionary<string, string> Headers = new()
    {
        ["Name"] = "名称",
        ["Kind"] = "类型 Shaft=轴 / Blade=叶片",
        ["Multiplier"] = "阶次 / 倍数",
        ["Enabled"] = "启用",
        ["Evidence"] = "依据 / 证据",
        ["FrequencyHz"] = "频率 Hz",
        ["DampingRatio"] = "阻尼比 ζ",
        ["ExtraBandHz"] = "附加半宽 Hz",
        ["Origin"] = "来源类别（见帮助）",
        ["Source"] = "来源说明",
        ["Location"] = "结构位置 / 边界",
        ["Notes"] = "备注",
        ["Rpm"] = "转速 rpm",
        ["Fixed"] = "扫描固定",
        ["Mode"] = "模态",
        ["Order"] = "阶次名称",
        ["Plan"] = "方案",
        ["Gear"] = "挡位",
        ["EffectiveOrder"] = "有效阶次",
        ["ExactRpm"] = "精确交叉 rpm",
        ["LowerRpm"] = "敏感下限 rpm",
        ["UpperRpm"] = "敏感上限 rpm",
        ["InRange"] = "与分析范围相交",
        ["OrderNumber"] = "有效阶次",
        ["LowerHz"] = "激励下限 Hz",
        ["UpperHz"] = "激励上限 Hz",
        ["ModeHz"] = "模态频率 Hz",
        ["HalfWidthHz"] = "敏感半宽 Hz",
        ["ClearanceHz"] = "区间净距 Hz",
        ["Near"] = "潜在接近",
        ["Status"] = "状态",
        ["Position"] = "挡位序号",
        ["OriginalRpm"] = "原转速 rpm",
        ["CandidateRpm"] = "候选转速 rpm",
        ["Contacts"] = "接近组合数",
        ["Cost"] = "局部代价",
        ["BaselineGear"] = "基准挡位",
        ["TargetGear"] = "目标挡位",
        ["BaselineRpm"] = "基准 rpm",
        ["TargetRpm"] = "目标 rpm",
        ["DeltaRpm"] = "目标减基准 rpm",
        ["BaselineContacts"] = "基准接近数",
        ["TargetContacts"] = "目标接近数",
        ["Note"] = "比较口径",
        ["AbsoluteBandHz"] = "绝对半宽 Hz",
        ["SpeedToleranceRpm"] = "转速容差 rpm",
        ["NearGears"] = "接近挡位数",
        ["ChangedStatuses"] = "状态变化数",
        ["MinimumClearanceHz"] = "最小净距 Hz",
        ["GearNames"] = "接近挡位",
        ["Title"] = "任务",
        ["Method"] = "方法与工况",
        ["Owner"] = "负责人",
        ["State"] = "验证状态（见帮助）",
        ["Updated"] = "更新时间",
        ["Severity"] = "提示级别",
        ["Object"] = "对象",
        ["Finding"] = "发现",
        ["Action"] = "操作 / 建议",
        ["Time"] = "时间",
        ["Detail"] = "详情",
        ["Revision"] = "修订",
        ["Line"] = "CSV行号",
        ["Column"] = "字段",
        ["Message"] = "问题"
    };
}
