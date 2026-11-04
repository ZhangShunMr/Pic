using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.IO;

namespace AcousticSeal.DesktopLogic;

public sealed class EditorField : Bindable
{
    private string value = "";
    private bool flag;
    public string Key { get; init; } = "";
    public string Label { get; init; } = "";
    public string Help { get; init; } = "";
    public string Value { get => value; set => Set(ref this.value, value); }
    public bool Flag { get => flag; set => Set(ref flag, value); }
    public bool IsFlag { get; init; }
    public bool IsChoice => Choices.Count > 0;
    public bool IsText => !IsFlag && !IsChoice;
    public List<Choice> Choices { get; init; } = [];
}

public sealed class LocationChoice : Bindable
{
    private bool selected;
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public bool Selected { get => selected; set => Set(ref selected, value); }
}

public sealed class BandDraft : Bindable
{
    private string frequency = "";
    private string level = "";
    private string note = "";
    public string Frequency { get => frequency; set => Set(ref frequency, value); }
    public string Level { get => level; set => Set(ref level, value); }
    public string Note { get => note; set => Set(ref note, value); }
}

public sealed class EntityEditor : Bindable
{
    private bool dirty;
    private readonly object model;
    public ObservableCollection<EditorField> Fields { get; } = [];
    public ObservableCollection<BandDraft> Bands { get; } = [];
    public ObservableCollection<LocationChoice> Locations { get; } = [];
    public bool HasBands => model is AcousticTrial;
    public bool Dirty { get => dirty; private set => Set(ref dirty, value); }
    public string Identity => model.GetType().GetProperty("Id")?.GetValue(model)?.ToString() ?? "参数";

    public EntityEditor(object source, AcousticProject project)
    {
        model = source;
        foreach (var property in model.GetType().GetProperties())
        {
            if (!LabelsByKey.TryGetValue(property.Name, out var label)) continue;
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (type != typeof(string) && type != typeof(double) && type != typeof(bool) &&
                type != typeof(Guid) && type != typeof(DateTimeOffset) && !type.IsEnum) continue;
            var raw = property.GetValue(model);
            var choices = new List<Choice>();
            if (type.IsEnum)
            {
                choices.AddRange(Enum.GetValues(type).Cast<object>().Select(value =>
                    new Choice(value.ToString()!, EnumLabel(value))));
            }
            if (type == typeof(Guid))
            {
                var items = property.Name == "ConditionId"
                    ? project.Conditions.Select(item => new Choice(item.Id.ToString(), item.Name))
                    : project.Trials.Where(item => property.Name != "BaselineId" || item.Kind == TrialKind.Baseline)
                        .Select(item => new Choice(item.Id.ToString(), item.Name + " · " + item.Kind.Text()));
                choices.Add(new Choice(Guid.Empty.ToString(), "请选择引用对象"));
                choices.AddRange(items);
            }
            if (property.Name == "Weighting") choices = [new("", "草稿空白"), new("A", "A"), new("C", "C"), new("Z", "Z")];
            if (property.Name == "BandType") choices = [new("", "草稿空白"), new("octave", "倍频程"), new("third-octave", "三分之一倍频程"), new("custom", "自定义")];
            var field = new EditorField
            {
                Key = property.Name, Label = label,
                Help = HelpByKey.GetValueOrDefault(property.Name, ""),
                IsFlag = type == typeof(bool), Flag = raw is true,
                Value = raw is DateTimeOffset time ? time.ToString("O", CultureInfo.InvariantCulture) : Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "",
                Choices = choices
            };
            if (model is AnalysisSettings settings)
            {
                var description = settings.Describe().FirstOrDefault(item => item.Key == property.Name);
                if (description is not null)
                {
                    field = new EditorField
                    {
                        Key = field.Key, Label = description.Label + " (" + description.Unit + ")",
                        Value = field.Value,
                        Help = $"工程初值 {description.Default}；允许 {description.Minimum}—{description.Maximum}，不是标准限值。"
                    };
                }
            }
            Fields.Add(field);
            field.PropertyChanged += (_, _) => Dirty = true;
        }
        if (model is AcousticTrial trial)
        {
            foreach (var location in project.Locations)
            {
                var choice = new LocationChoice { Id = location.Id, Name = location.Name, Selected = trial.LocationIds.Contains(location.Id) };
                choice.PropertyChanged += (_, _) => Dirty = true;
                Locations.Add(choice);
            }
            foreach (var band in trial.Bands.OrderBy(item => item.Frequency))
            {
                AddBand(new BandDraft
                {
                    Frequency = band.Frequency.ToString("G17", CultureInfo.InvariantCulture),
                    Level = band.Level?.ToString("G17", CultureInfo.InvariantCulture) ?? "", Note = band.Note
                });
            }
        }
        Dirty = false;
    }

    public void AddBand(BandDraft? row = null)
    {
        if (Bands.Count >= Software.MaximumBands) throw new DomainValidationException("频带", "最多200行");
        row ??= new BandDraft();
        row.PropertyChanged += (_, _) => Dirty = true;
        Bands.Add(row);
        Dirty = true;
    }
    public void RemoveBand(BandDraft? row)
    {
        if (row is null) return;
        Bands.Remove(row);
        Dirty = true;
    }
    public void Paste(string text)
    {
        var rows = CsvParser.Read(text, ',');
        var parsed = new List<BandDraft>();
        foreach (var row in rows)
        {
            if (row.Cells.Count != 2) throw new DomainValidationException("粘贴第" + row.Line + "行", "须为两列：Hz,声级，无表头");
            ParseNumber(row.Cells[0], "粘贴频率");
            if (row.Cells[1].Trim() is not ("" or "NA" or "N/A" or "null")) ParseNumber(row.Cells[1], "粘贴声级");
            parsed.Add(new BandDraft { Frequency = row.Cells[0], Level = row.Cells[1] });
        }
        if (parsed.Count is < 1 or > 200) throw new DomainValidationException("粘贴", "须为1至200行");
        var check = parsed.Select(row => ParseNumber(row.Frequency, "频率")).ToList();
        if (check.Distinct().Count() != check.Count || check.Any(value => value is < 1 or > 100000))
            throw new DomainValidationException("粘贴频率", "频率重复或超出1—100000Hz");
        foreach (var row in parsed.Where(row => row.Level.Trim() is not ("" or "NA" or "N/A" or "null")))
            if (ParseNumber(row.Level, "声级") is < -100 or > 200) throw new DomainValidationException("粘贴声级", "超出-100—200dB");
        Bands.Clear();
        foreach (var row in parsed) AddBand(row);
    }

    public object Read()
    {
        // Work on a fresh copy so a rejected transaction leaves both saved inputs and draft intact.
        var copy = model switch
        {
            AcousticProject value => (object)ProjectCodec.Clone(value),
            SealLocation value => ProjectCodec.Clone(value),
            MeasurementCondition value => ProjectCodec.Clone(value),
            AcousticTrial value => ProjectCodec.Clone(value),
            TrialPair value => ProjectCodec.Clone(value),
            AnalysisSettings value => ProjectCodec.Clone(value),
            _ => throw new InvalidOperationException("未知编辑对象")
        };
        foreach (var field in Fields)
        {
            var property = copy.GetType().GetProperty(field.Key)!;
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            var text = field.Value.Trim();
            object? value;
            if (type == typeof(bool)) value = field.Flag;
            else if (type == typeof(string)) value = text;
            else if (type == typeof(Guid)) value = Guid.TryParse(text, out var id) ? id : throw new DomainValidationException(field.Label, "请选择有效对象");
            else if (type.IsEnum) value = Enum.Parse(type, text);
            else if (type == typeof(DateTimeOffset))
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(text, "(Z|[+-][0-9]{2}:[0-9]{2})$") ||
                    !DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                    throw new DomainValidationException(field.Label, "请输入带时区ISO时间，例如2026-11-04T10:00:00+08:00");
                value = time;
            }
            else value = text == "" && Nullable.GetUnderlyingType(property.PropertyType) is not null ? null : ParseNumber(text, field.Label);
            property.SetValue(copy, value);
        }
        if (copy is AcousticTrial trial)
        {
            trial.LocationIds = Locations.Where(item => item.Selected).Select(item => item.Id).ToList();
            trial.Bands = Bands.Where(row => !(string.IsNullOrWhiteSpace(row.Frequency) && string.IsNullOrWhiteSpace(row.Level) && string.IsNullOrWhiteSpace(row.Note)))
                .Select((row, index) => new BandMeasurement
                {
                    Frequency = ParseNumber(row.Frequency, $"频带第{index + 1}行频率"),
                    Level = row.Level.Trim() is "" or "NA" or "N/A" or "null" ? null : ParseNumber(row.Level, $"频带第{index + 1}行声级"),
                    Note = row.Note.Trim()
                }).OrderBy(row => row.Frequency).ToList();
        }
        return copy;
    }
    private static double ParseNumber(string text, string path)
    {
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new DomainValidationException(path, "请输入有限数值，使用句点小数；缺失声级可以留空");
        return value;
    }
    private static string EnumLabel(object value) => value switch
    {
        TrialKind item => item.Text(), SourceKind item => item.Text(), PairPurpose item => item.Text(),
        _ => value.ToString() ?? ""
    };
    private static readonly Dictionary<string, string> LabelsByKey = new()
    {
        ["Name"]="名称 *", ["Owner"]="负责人", ["VehicleProgramme"]="车辆项目", ["Objective"]="试验目标", ["Description"]="项目说明",
        ["Zone"]="区域", ["Material"]="封堵材料", ["Extent"]="覆盖范围", ["Procedure"]="封堵工艺", ["RecoveryProcedure"]="恢复工艺", ["Reference"]="照片/图纸编号", ["Notes"]="备注",
        ["Vehicle"]="车辆标识", ["Microphone"]="测点和安装位置", ["Excitation"]="声源与激励", ["Surface"]="路面或试验场", ["Gear"]="挡位", ["Weighting"]="频率计权", ["TimeDefinition"]="时间计权/积分定义", ["BandType"]="频带制", ["Instrument"]="仪器标识", ["Calibration"]="标定链路与状态", ["Unit"]="声级单位及参考", ["Speed"]="车速 (km/h)", ["Rpm"]="转速 (r/min)", ["Temperature"]="温度 (°C)", ["Duration"]="积分时长 (s)", ["SampleRate"]="采样率 (Hz)", ["Synchronization"]="同步定义", ["NonOverlappingBands"]="确认录入频带互不重叠",
        ["Kind"]="试验类型", ["ConditionId"]="工况 *", ["Timestamp"]="采集时间（含时区）", ["RepeatGroup"]="重复组名称", ["Source"]="数据来源", ["SourceNote"]="来源说明 *", ["OriginalFile"]="原始文件编号", ["Operator"]="操作人", ["Preparation"]="试验准备",
        ["BaselineId"]="基线试验 *", ["TargetId"]="目标试验 *", ["Purpose"]="配对用途",
        ["SpeedTolerance"]="车速容差", ["RpmTolerance"]="转速容差", ["TemperatureTolerance"]="温度容差", ["DurationTolerance"]="时长容差", ["DriftThreshold"]="漂移阈值", ["RecoveryThreshold"]="恢复阈值", ["RepeatabilityThreshold"]="重复性阈值", ["MinimumEffectDisplay"]="变化标记阈值", ["RequireSampleRateMatch"]="严格核对采样率", ["RequireSynchronizationMatch"]="严格核对同步定义"
    };
    private static readonly Dictionary<string,string> HelpByKey = new()
    {
        ["Name"]="必填。修改后点击“应用编辑”才进入项目。",
        ["SourceNote"]="描述模拟、人工、估算或实测声明的具体来源；不会自动认证实测。",
        ["NonOverlappingBands"]="只有两端均确认且频带完整、集合相同时，才计算录入范围的能量合成差。",
        ["RepeatGroup"]="同组、同类型、位置与来源一致且工况两两可比，至少两个有效样本才有样本标准差。",
        ["Calibration"]="记录标定编号/链路。非空且两端一致仅是可比条件，不构成标定认证。",
        ["Timestamp"]="ISO8601格式，必须带Z或±HH:mm时区。",
        ["Unit"]="仅支持 dB re 20 µPa；不会自动变换单位或再次计权。",
        ["Speed"]="允许0—500；缺失可保存草稿但阻断可比计算。",
        ["Rpm"]="允许0—30000；缺失可保存草稿但阻断可比计算。",
        ["Temperature"]="允许-80—100；缺失可保存草稿但阻断可比计算。",
        ["Duration"]="允许0.001—86400；缺失可保存草稿但阻断可比计算。"
    };
}
