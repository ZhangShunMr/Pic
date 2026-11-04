using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TrimContact.Desktop.ViewModels;

namespace TrimContact.Desktop.Controls;

public abstract class WorkspaceDrawing : FrameworkElement
{
    public static readonly DependencyProperty WorkspaceProperty = DependencyProperty.Register(
        nameof(Workspace), typeof(WorkspaceViewModel), typeof(WorkspaceDrawing),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, Changed));
    private WorkspaceViewModel? subscribed;

    protected WorkspaceDrawing()
    {
        ClipToBounds = true;
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    public WorkspaceViewModel? Workspace
    {
        get => (WorkspaceViewModel?)GetValue(WorkspaceProperty);
        set => SetValue(WorkspaceProperty, value);
    }

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        ((WorkspaceDrawing)target).Subscribe();
    }

    private void Subscribe()
    {
        Unsubscribe();
        subscribed = Workspace;
        if (subscribed is not null) subscribed.PropertyChanged += OnChanged;
        InvalidateVisual();
    }

    private void Unsubscribe()
    {
        if (subscribed is not null) subscribed.PropertyChanged -= OnChanged;
        subscribed = null;
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e) => InvalidateVisual();

    protected void Text(DrawingContext dc, string text, double x, double y, Brush? brush = null, double size = 12)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface("Microsoft YaHei UI"), size, brush ?? Brushes.DarkSlateGray,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        dc.DrawText(formatted, new Point(x, y));
    }

    protected static string N(double value) => value.ToString("G5", CultureInfo.InvariantCulture);
    protected static bool Finite(double x, double y) => double.IsFinite(x) && double.IsFinite(y);
}
