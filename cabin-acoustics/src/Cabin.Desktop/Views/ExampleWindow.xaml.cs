using System.Windows;

namespace Cabin.Desktop.Views;

public partial class ExampleWindow : Window
{
    public ExampleWindow()
    {
        InitializeComponent();
    }

    public int Choice { get; private set; }

    private void Accept(object sender, RoutedEventArgs e)
    {
        Choice = FourSeats.IsChecked == true ? 1 : 2;
        DialogResult = true;
    }
}
