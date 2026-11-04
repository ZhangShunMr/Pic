using System.Windows;
using Cabin.Core.IO;

namespace Cabin.Desktop.Views;

public partial class ImportWindow : Window
{
    public ImportWindow(CsvPreview preview)
    {
        InitializeComponent();
        DataContext = preview;
    }

    private void Apply(object sender, RoutedEventArgs e)
    {
        if (DataContext is CsvPreview { CanApply: true })
            DialogResult = true;
    }
}
