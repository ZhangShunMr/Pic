using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using Cabin.Desktop.Services;
using Cabin.Desktop.ViewModels;

namespace Cabin.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow() : this(new DesktopServices())
    {
    }

    public MainWindow(IDesktopServices services)
    {
        InitializeComponent();
        var vm = new WorkspaceViewModel(services);
        DataContext = vm;
        vm.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(WorkspaceViewModel.Section))
            {
                SelectSection(Navigation.Items, vm.Section);
                WorkspaceScroll.ScrollToTop();
            }
        };
    }

    private static void SelectSection(ItemCollection items, string section)
    {
        foreach (var item in items.OfType<TreeViewItem>())
        {
            if (item.Tag is string tag && tag == section)
                item.IsSelected = true;
            SelectSection(item.Items, section);
        }
    }

    public FrameworkElement FieldVisual => FieldDrawing;

    public void ShowFieldSettings()
    {
        FieldSettings.IsExpanded = true;
        WorkspaceScroll.ScrollToTop();
    }

    public void ShowFullField()
    {
        FieldSettings.IsExpanded = false;
        WorkspaceScroll.UpdateLayout();
        WorkspaceScroll.ScrollToBottom();
    }

    private void Navigate(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is WorkspaceViewModel vm && e.NewValue is TreeViewItem { Tag: string section })
            vm.Section = section;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is WorkspaceViewModel vm)
            e.Cancel = !vm.CanClose();
    }

    private void Exit(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
