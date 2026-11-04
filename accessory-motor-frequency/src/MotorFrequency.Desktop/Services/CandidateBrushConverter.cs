using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MotorFrequency.Core.Services;

namespace MotorFrequency.Desktop.Services;

public sealed class CandidateBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(
            ChartGeometry.CandidateColor(value as string ?? "rotation")));
        brush.Freeze();
        return brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return DependencyProperty.UnsetValue;
    }
}
