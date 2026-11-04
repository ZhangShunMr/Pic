using System.Windows.Data;

namespace Avas.Desktop;

public sealed class Edit : Binding
{
    public Edit(string path) : base(path)
    {
        Mode = BindingMode.TwoWay;
        UpdateSourceTrigger = UpdateSourceTrigger.LostFocus;
        ValidatesOnExceptions = true;
        NotifyOnValidationError = true;
        NotifyOnSourceUpdated = true;
        ConverterCulture = System.Globalization.CultureInfo.InvariantCulture;
    }
}
