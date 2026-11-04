using System.Collections;
using System.ComponentModel;
using System.Globalization;
using Cabin.Desktop.Infrastructure;

namespace Cabin.Desktop.ViewModels;

public sealed class InputField : ObservableObject, INotifyDataErrorInfo
{
    private string text;
    private readonly Action changed;
    private readonly double? minimum;
    private readonly double? maximum;
    private readonly int maximumLength;
    private readonly bool required;
    private string? error;

    public InputField(string label, string value, Action changed, string hint = "",
        bool required = false, int maximumLength = 3000, double? minimum = null, double? maximum = null)
    {
        Label = label;
        text = value;
        this.changed = changed;
        Hint = hint;
        this.minimum = minimum;
        this.maximum = maximum;
        this.maximumLength = maximumLength;
        this.required = required;
        Validate();
    }

    public static InputField Number(string label, double value, double min, double max, Action changed, string hint)
    {
        return new(label, value.ToString("G12", CultureInfo.InvariantCulture), changed,
            hint, true, 40, min, max);
    }

    public string Label { get; }
    public string Hint { get; }
    public bool IsNumeric => minimum.HasValue;
    public bool HasErrors => error is not null;
    public string Error => error ?? "";
    public string Text
    {
        get => text;
        set
        {
            if (!Set(ref text, value))
                return;
            Validate();
            changed();
        }
    }

    public double Value
    {
        get
        {
            Validate();
            if (HasErrors)
                throw new ArgumentException(Label + "：" + error);
            return double.Parse(Text, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
    }

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        return error is null ? Array.Empty<string>() : new[] { error };
    }

    public void EnsureValid()
    {
        Validate();
        if (HasErrors)
            throw new ArgumentException(Label + "：" + error);
    }

    private void Validate()
    {
        string? next = null;
        if (required && string.IsNullOrWhiteSpace(text))
            next = "不能为空";
        else if (text.Length > maximumLength)
            next = $"不能超过{maximumLength}字符";
        else if (IsNumeric)
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number))
                next = "请输入有限数值，使用小数点";
            else if (number < minimum || number > maximum)
                next = $"范围为{minimum}至{maximum}";
        }
        if (error == next)
            return;
        error = next;
        Notify(nameof(HasErrors));
        Notify(nameof(Error));
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(Text)));
    }
}
