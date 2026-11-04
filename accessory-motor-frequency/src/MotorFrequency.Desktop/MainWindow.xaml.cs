using System.ComponentModel;
using System.Windows;
using MotorFrequency.Application;
using MotorFrequency.Desktop.Services;

namespace MotorFrequency.Desktop;

public partial class MainWindow : Window
{
    public WorkspaceViewModel Workspace { get; }

    public MainWindow()
    {
        InitializeComponent();
        Workspace = new WorkspaceViewModel(new NativeInteraction(this));
        DataContext = Workspace;
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        e.Cancel = !Workspace.TryClose();
    }

    private void ExitClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void HelpClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "先新建或打开项目，在左侧目录进入各工作页。编辑条件后点击右侧“应用条件”。\n\n"
            + "频谱CSV表头：frequency_hz,amplitude；UTF-8，频率递增，最多10000点。需要转速、单位、参考量、工况和来源。先预检再确认导入。\n\n"
            + "机械转频n/60；同步无刷电频p×n/60；有刷只给单刷片通过K×n/60。电频不等于实际电磁力阶次。频率邻近、频带交叠均不证明根因或共振。\n\n"
            + "项目内嵌原始点。再次保存保留.bak；从打开对话框选择备份可恢复为副本。外部修改冲突会拒绝覆盖。\n\n"
            + "完整步骤、参数边界、示例和验收限制见随源代码交付的README及docs目录。",
            "使用说明与计算边界", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
