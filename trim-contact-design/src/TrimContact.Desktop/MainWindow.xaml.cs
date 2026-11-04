using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using TrimContact.Desktop.ViewModels;

namespace TrimContact.Desktop;

public partial class MainWindow : Window
{
    private readonly WorkspaceViewModel workspace;
    private readonly HashSet<ValidationError> errors = [];
    private bool notifying;

    public MainWindow()
    {
        InitializeComponent();
        workspace = new WorkspaceViewModel(new DesktopInteraction());
        DataContext = workspace;
        workspace.DraftReplaced += (_, _) => errors.Clear();
        AddHandler(Binding.SourceUpdatedEvent, new EventHandler<DataTransferEventArgs>(OnSourceUpdated));
        AddHandler(Validation.ErrorEvent, new EventHandler<ValidationErrorEventArgs>(OnValidationError));
        PreviewKeyDown += CommitShortcut;
        Closing += OnClosing;
        Closed += (_, _) => workspace.Dispose();
    }

    private void OnSourceUpdated(object? sender, DataTransferEventArgs e)
    {
        if (notifying) return;
        notifying = true;
        try { workspace.NotifyDraftEdited(); }
        finally { notifying = false; }
    }

    private void OnValidationError(object? sender, ValidationErrorEventArgs e)
    {
        if (e.Action == ValidationErrorEventAction.Added) errors.Add(e.Error);
        else errors.Remove(e.Error);
        workspace.SetBindingErrors(errors.Count);
    }

    private void CommitShortcut(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F5 || (Keyboard.Modifiers == ModifierKeys.Control
            && e.Key is Key.S or Key.O or Key.N or Key.Z or Key.Y)) CommitFocusedEditor();
    }

    private void CommitFocusedEditor()
    {
        if (Keyboard.FocusedElement is TextBox text)
            text.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        var node = Keyboard.FocusedElement as DependencyObject;
        while (node is not null && node is not DataGrid)
            node = VisualTreeHelper.GetParent(node);
        if (node is DataGrid grid)
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        CommitFocusedEditor();
        e.Cancel = !workspace.RequestClose();
    }
}
