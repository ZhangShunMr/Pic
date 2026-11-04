using System.IO;
using System.Collections.ObjectModel;
using System.Globalization;
using Blower.Core.Domain;

namespace Blower.Desktop.ViewModels;

public sealed class EditorField : ObservableObject
{
    private string value;
    private readonly Action<string> apply;
    public string Label { get; }
    public string Help { get; }
    public string Original { get; }
    public IReadOnlyList<string> Choices { get; }
    public bool IsChoice => Choices.Count > 0;
    public bool IsText => !IsChoice;
    public bool Changed => Value != Original;

    public string Value
    {
        get => value;
        set
        {
            if (Set(ref this.value, value))
            {
                Raise(nameof(Changed));
            }
        }
    }

    public EditorField(string label, string initial, string help, Action<string> apply,
        IReadOnlyList<string>? choices = null)
    {
        Label = label;
        value = initial;
        Original = initial;
        Help = help;
        this.apply = apply;
        Choices = choices ?? [];
    }

    public void Apply()
    {
        try
        {
            apply(Value);
        }
        catch (Exception e) when (e is FormatException or OverflowException)
        {
            throw new InvalidDataException(Label + "：" + e.Message);
        }
    }
}

public sealed class EditorForm : ObservableObject
{
    public ObservableCollection<EditorField> Fields { get; } = [];
    public string Title { get; set; } = "属性";
    public bool Changed => Fields.Any(x => x.Changed);

    public void Clear(string title)
    {
        Fields.Clear();
        Title = title;
        Raise(nameof(Title));
        Raise(nameof(Changed));
    }

    private void Add(EditorField field)
    {
        field.PropertyChanged += (_, _) => Raise(nameof(Changed));
        Fields.Add(field);
    }

    public void Text(string label, string value, Action<string> setter, string help = "")
    {
        Add(new(label, value, help, setter));
    }

    public void Number(string label, double value, Action<double> setter, string help)
    {
        Add(new(label, value.ToString("G", CultureInfo.InvariantCulture), help, text =>
        {
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || !double.IsFinite(number))
            {
                throw new FormatException("请输入有限数值，使用英文小数点");
            }
            setter(number);
        }));
    }

    public void Integer(string label, int value, Action<int> setter, string help)
    {
        Add(new(label, value.ToString(CultureInfo.InvariantCulture), help, text =>
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
            {
                throw new FormatException("请输入整数");
            }
            setter(number);
        }));
    }

    public void NullableNumber(string label, double? value, Action<double?> setter, string help)
    {
        Add(new(label, value?.ToString("G", CultureInfo.InvariantCulture) ?? "", help, text =>
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                setter(null);
                return;
            }
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                || !double.IsFinite(number))
            {
                throw new FormatException("请输入有限数值，未知时留空");
            }
            setter(number);
        }));
    }

    public void Boolean(string label, bool value, Action<bool> setter, string help = "")
    {
        Add(new(label, value ? "是" : "否", help, text => setter(text == "是"), ["否", "是"]));
    }

    public void Source(string label, Origin value, Action<Origin> setter)
    {
        var choices = Enum.GetValues<Origin>().Select(Labels.Source).ToArray();
        Add(new(label, Labels.Source(value), "声明实测不等于软件认证其测量质量", text =>
        {
            setter(Enum.GetValues<Origin>().Single(x => Labels.Source(x) == text));
        }, choices));
    }

    public void OrderType(OrderKind value, Action<OrderKind> setter)
    {
        var choices = Enum.GetValues<OrderKind>().Select(Labels.Kind).ToArray();
        Add(new("阶次类型", Labels.Kind(value), "叶片通过倍频的有效阶次为叶片数乘以倍数", text =>
        {
            setter(Enum.GetValues<OrderKind>().Single(x => Labels.Kind(x) == text));
        }, choices));
    }

    public void CheckStatus(CheckState value, Action<CheckState> setter)
    {
        var choices = Enum.GetValues<CheckState>().Select(Labels.State).ToArray();
        Add(new("验证状态", Labels.State(value), "已记录证据时必须填写证据内容", text =>
        {
            setter(Enum.GetValues<CheckState>().Single(x => Labels.State(x) == text));
        }, choices));
    }

    public void Apply()
    {
        foreach (var field in Fields)
        {
            field.Apply();
        }
    }
}
