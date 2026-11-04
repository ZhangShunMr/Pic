using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Avas.Core.Analysis;
using Avas.Workspace;

namespace Avas.Desktop;

public partial class MainWindow : Window
{
    private readonly WorkspaceViewModel workspace;
    private readonly HashSet<ValidationError> bindingErrors = [];

    public MainWindow(WorkspaceViewModel workspace)
    {
        this.workspace = workspace;
        InitializeComponent();
        DataContext = workspace;
        AddHandler(Validation.ErrorEvent, new EventHandler<ValidationErrorEventArgs>(BindingError));
        AddHandler(Binding.SourceUpdatedEvent, new EventHandler<DataTransferEventArgs>(EditorSourceUpdated));
        CommandManager.AddPreviewExecutedHandler(this, BeforeCommand);
        PreviewKeyDown += BeforeShortcut;
    }

    private void EditorSourceUpdated(object? sender, DataTransferEventArgs e)
    {
        workspace.MarkDraft();
    }

    private void BindingError(object? sender, ValidationErrorEventArgs e)
    {
        if (e.Action == ValidationErrorEventAction.Added)
            bindingErrors.Add(e.Error);
        else
            bindingErrors.Remove(e.Error);
        workspace.BindingErrorCount = bindingErrors.Count;
        workspace.Notify();
    }

    private void BeforeShortcut(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 || (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.S or Key.O or Key.N))
            FlushEditor();
    }

    private void BeforeCommand(object sender, ExecutedRoutedEventArgs e) => FlushEditor();

    private void FlushEditor()
    {
        if (Keyboard.FocusedElement is TextBox text)
            text.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (Keyboard.FocusedElement is FrameworkElement element)
        {
            var parent = System.Windows.Media.VisualTreeHelper.GetParent(element);
            while (parent is not null && parent is not DataGrid)
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
            if (parent is DataGrid grid)
            {
                grid.CommitEdit(DataGridEditingUnit.Cell, true);
                grid.CommitEdit(DataGridEditingUnit.Row, true);
            }
        }
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        FlushEditor();
        e.Cancel = !workspace.CanClose();
        if (!e.Cancel)
            workspace.DisposeServices();
    }

    private void CoverageChanged(object sender, RoutedEventArgs e) => Field?.InvalidateVisual();

    private void PointPicked(object? sender, Guid id)
    {
        workspace.Execute(() =>
        {
            if (workspace.HasDraft || workspace.BindingErrorCount > 0)
                throw new ArgumentException("请先应用或放弃字段编辑，再选择观察点。");
            workspace.SelectListener(id);
        });
    }

    private void ResultSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (((DataGrid)sender).SelectedItem is PointResult { ListenerId: { } id })
            PointPicked(sender, id);
    }

    private void ShowHelp(object sender, RoutedEventArgs e)
    {
        var window = new HelpWindow { Owner = this };
        window.ShowDialog();
    }
}
