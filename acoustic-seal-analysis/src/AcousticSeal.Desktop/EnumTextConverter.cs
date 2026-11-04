using System.Globalization;
using System.Windows.Data;
using AcousticSeal.Core.Domain;
namespace AcousticSeal.Desktop;
public sealed class EnumTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        ResultStatus status => status.Text(), PairPurpose purpose => purpose.Text(),
        SourceKind source => source.Text(), TrialKind kind => kind.Text(), _ => value
    };
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
