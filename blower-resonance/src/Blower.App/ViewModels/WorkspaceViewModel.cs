using System.ComponentModel;
using System.Text.Json;
using Blower.Core.Analysis;
using Blower.Core.Domain;
using Blower.Core.IO;
using Blower.Core.Operations;

namespace Blower.App.ViewModels;

public sealed class WorkspaceViewModel(ProjectStore store) : INotifyPropertyChanged
{
    private readonly ProjectSession session = new();
    private Action? pending;
    public event PropertyChangedEventHandler? PropertyChanged;
    public Project Project => session.Current;
    public Project Draft { get; private set; } = new();
    public AnalysisResult? Result { get; private set; }
    public ScanResult? Scan { get; private set; }
    public RuleStudyResult? Study { get; private set; }
    public string StudyBands { get; set; } = "5,20,40";
    public string StudyTolerances { get; set; } = "0,20,50";
    public IReadOnlyList<ReviewFinding> Review => QualityReview.Inspect(Project);
    public string Kind { get; private set; } = "project";
    public string SelectedId { get; private set; } = "";
    public string PlanId { get; private set; } = "";
    public string CompareId { get; set; } = "";
    public string Notice { get; private set; } = "从空白项目开始，或在左侧载入模拟示例。";
    public bool Error { get; private set; }
    public string Confirmation { get; private set; } = "";
    public string CsvKind { get; set; } = "gears";
    public string CsvText { get; set; } = "name,rpm,fixed\n低挡,900,false\n中挡,1800,false\n高挡,3000,false";
    public CsvPreview? Preview { get; private set; }
    public string ResultFilter { get; set; } = "near";
    public string Search { get; set; } = "";
    public double? ChartCeiling { get; set; }
    public bool ShowLibrary { get; private set; }
    public bool ShowExchange { get; private set; }
    public bool ShowHelp { get; private set; }
    public bool Busy { get; private set; }
    public string DownloadUrl { get; private set; } = "";
    public string DownloadName { get; private set; } = "";
    public string LastSavedPath { get; private set; } = "";
    public bool Dirty => session.Dirty;
    public bool CanUndo => session.CanUndo;
    public bool CanRedo => session.CanRedo;
    public bool EditorDirty => Serialize(Draft) != Serialize(Project);
    public bool ReadOnly => Project.Archived;
    public SpeedPlan? ActivePlan => Project.Plans.FirstOrDefault(x => x.Id == PlanId);
    public SpeedPlan? DraftPlan => Draft.Plans.FirstOrDefault(x => x.Id == PlanId);
    public Gear? DraftGear => Draft.Plans.SelectMany(x => x.Gears).FirstOrDefault(x => x.Id == SelectedId);
    public Mode? DraftMode => Draft.Modes.FirstOrDefault(x => x.Id == SelectedId);
    public Excitation? DraftOrder => Draft.Orders.FirstOrDefault(x => x.Id == SelectedId);
    public VerificationTask? DraftCheck => Draft.Checks.FirstOrDefault(x => x.Id == SelectedId);
    public IReadOnlyList<Issue> Issues => Validation.Inspect(Project, true);
    public IReadOnlyList<Issue> DraftIssues => Validation.Inspect(Draft).Where(x => x.Blocks).ToList();
    public IReadOnlyList<StoredProject> Library => store.List();
    public ChartModel? Chart => Result == null ? null : ChartModel.Create(Project, Result, PlanId, ChartCeiling);
    public PlanSummary? Summary => Result?.Summaries.FirstOrDefault(x => x.PlanId == PlanId);
    public IEnumerable<Contact> VisibleContacts => (Result?.Contacts ?? [])
        .Where(x => x.PlanId == PlanId)
        .Where(x => ResultFilter != "near" || x.Near)
        .Where(x => string.IsNullOrWhiteSpace(Search)
            || (x.Gear + x.Mode + x.Order).Contains(Search, StringComparison.OrdinalIgnoreCase))
        .OrderBy(x => x.ClearanceHz);

    public PlanComparison? Comparison
    {
        get
        {
            if (Result == null || PlanId == CompareId || !Project.Plans.Any(x => x.Id == CompareId))
            {
                return null;
            }
            return Comparisons.Compare(Project, Result, PlanId, CompareId);
        }
    }

    private static string Serialize(Project p) => JsonSerializer.Serialize(p, ProjectCodec.Options);

    public void Initialize()
    {
        Draft = ProjectCodec.Clone(Project);
    }

    public void Notify()
    {
        PropertyChanged?.Invoke(this, new(nameof(Project)));
    }

    public void Message(string message, bool error = false)
    {
        Notice = message;
        Error = error;
        Notify();
    }

    public void Attempt(Action action, string? success = null)
    {
        try
        {
            action();
            if (success != null)
            {
                Notice = success;
            }
            Error = false;
        }
        catch (Exception error) when (error is InvalidOperationException or InvalidDataException
            or IOException or JsonException or ArgumentException or UnauthorizedAccessException)
        {
            Notice = error.Message;
            Error = true;
        }
        Notify();
    }

    private void Confirm(string text, Action action)
    {
        Confirmation = text;
        pending = action;
        Notify();
    }

    public void ConfirmPending()
    {
        var action = pending;
        pending = null;
        Confirmation = "";
        if (action != null)
        {
            Attempt(action);
        }
    }

    public void CancelPending()
    {
        pending = null;
        Confirmation = "";
        Notify();
    }

    private void GuardReplace(Action action, string description)
    {
        if (Dirty || EditorDirty)
        {
            Confirm(description + "；当前未保存项目或属性草稿将离开。可取消后先应用属性并保存。", action);
        }
        else
        {
            Attempt(action);
        }
    }

    public void Select(string kind, string id = "", string? planId = null)
    {
        void SelectNow()
        {
            Kind = kind;
            SelectedId = id;
            if (planId != null)
            {
                PlanId = planId;
            }
            Draft = ProjectCodec.Clone(Project);
            Notify();
        }
        if (EditorDirty)
        {
            Confirm("右侧有未应用的属性；切换对象会放弃这些属性。", SelectNow);
        }
        else
        {
            SelectNow();
        }
    }

    private void Refresh(bool inputChanged = true)
    {
        Draft = ProjectCodec.Clone(Project);
        if (!Project.Plans.Any(x => x.Id == PlanId))
        {
            PlanId = Project.Plans.FirstOrDefault()?.Id ?? "";
        }
        if (!Project.Plans.Any(x => x.Id == CompareId) || CompareId == PlanId)
        {
            CompareId = Project.Plans.FirstOrDefault(x => x.Id != PlanId)?.Id ?? "";
        }
        if (inputChanged)
        {
            Scan = null;
            Study = null;
            Preview = null;
        }
        Result = Issues.Any(x => x.Blocks) ? null : Engine.Run(Project);
        DownloadUrl = "";
        Notify();
    }

    private void Change(string action, Action<Project> change, bool input = true, bool allowArchived = false)
    {
        session.Change(action, change, input, allowArchived);
        Refresh(input);
        Notice = action + "完成";
        Error = false;
    }

    public void NewProject()
    {
        GuardReplace(() =>
        {
            session.Load(new());
            Kind = "project";
            SelectedId = "";
            Refresh();
            Notice = "已建立空白项目；先填写项目来源并添加阶次、模态及挡位。";
        }, "新建空白项目");
    }

    public void LoadDemo()
    {
        GuardReplace(() =>
        {
            session.Load(Demo.Create());
            Kind = "project";
            Refresh();
            Notice = "已载入40叶片、三模态、双方案模拟示例；所有数值仅供核对。";
        }, "载入模拟示例");
    }

    public void ApplyEditor()
    {
        Attempt(() =>
        {
            if (Kind == "gear" && ActivePlan?.Locked == true)
            {
                throw new InvalidOperationException("当前方案已锁定；先选中方案并解除锁定");
            }
            var candidate = ProjectCodec.Clone(Draft);
            var input = ProjectCodec.InputHash(candidate) != ProjectCodec.InputHash(Project);
            Change("应用" + EditorTitle, p =>
            {
                p.Name = candidate.Name;
                p.Owner = candidate.Owner;
                p.Vehicle = candidate.Vehicle;
                p.Notes = candidate.Notes;
                p.Source = candidate.Source;
                p.Origin = candidate.Origin;
                p.Settings = candidate.Settings;
                p.Orders = candidate.Orders;
                p.Modes = candidate.Modes;
                p.Plans = candidate.Plans;
                p.Checks = candidate.Checks;
                foreach (var task in p.Checks.Where(x => x.Id == SelectedId && Kind == "check"))
                {
                    task.Updated = DateTimeOffset.UtcNow;
                }
            }, input);
        });
    }

    public string EditorTitle => Kind switch
    {
        "settings" => "计算与扫描规则",
        "mode" => "模态属性",
        "order" => "激励阶次",
        "plan" => "方案属性",
        "gear" => "挡位属性",
        "check" => "验证记录",
        _ => "项目属性"
    };

    public void RevertEditor()
    {
        Draft = ProjectCodec.Clone(Project);
        Message("已撤回尚未应用的属性");
    }

    private void RequireCleanEditor()
    {
        if (EditorDirty)
        {
            throw new InvalidOperationException("右侧属性尚未应用；请先应用属性或撤回修改");
        }
    }

    public void Add(string kind)
    {
        Attempt(() =>
        {
            RequireCleanEditor();
            var id = "";
            Change("新增" + kind, p =>
            {
                switch (kind)
                {
                    case "order":
                        var order = new Excitation { Name = "阶次 " + (p.Orders.Count + 1) };
                        var existing = p.Orders.Where(x => x.Enabled)
                            .Select(x => x.EffectiveOrder(p.Settings.Blades)).ToHashSet();
                        while (existing.Contains(order.Multiplier))
                        {
                            order.Multiplier++;
                        }
                        p.Orders.Add(order);
                        id = order.Id;
                        break;
                    case "mode":
                        var mode = new Mode { Name = "模态 " + (p.Modes.Count + 1), Origin = p.Origin };
                        p.Modes.Add(mode);
                        id = mode.Id;
                        break;
                    case "plan":
                        var plan = new SpeedPlan { Name = "方案 " + (p.Plans.Count + 1) };
                        p.Plans.Add(plan);
                        PlanId = plan.Id;
                        id = plan.Id;
                        break;
                    case "gear":
                        var active = p.Plans.FirstOrDefault(x => x.Id == PlanId)
                            ?? throw new InvalidOperationException("请先新增或选中一个方案");
                        if (active.Locked)
                        {
                            throw new InvalidOperationException("方案已锁定");
                        }
                        var rpm = active.Gears.Count == 0 ? p.Settings.MinRpm
                            : active.Gears[^1].Rpm + Math.Max(300, p.Settings.MinimumGapRpm);
                        var gear = new Gear { Name = (active.Gears.Count + 1) + "挡", Rpm = rpm };
                        active.Gears.Add(gear);
                        id = gear.Id;
                        break;
                    default:
                        throw new InvalidOperationException("不支持的新增对象");
                }
            });
            Select(kind, id, PlanId);
        });
    }

    public void DeleteSelected()
    {
        Confirm("删除当前对象？若存在验证记录引用，将阻止删除。此操作可撤销。", () =>
        {
            RequireCleanEditor();
            Change("删除对象", p => ProjectCommands.RemoveObject(p, Kind, SelectedId));
            Select("project");
        });
    }

    public void CopyPlan()
    {
        Attempt(() =>
        {
            RequireCleanEditor();
            var id = "";
            Change("复制方案", p =>
            {
                var source = p.Plans.Single(x => x.Id == PlanId);
                var copy = ProjectCommands.CopyPlan(source, source.Name + " · 副本");
                p.Plans.Add(copy);
                id = copy.Id;
            });
            PlanId = id;
            Select("plan", id, id);
        });
    }

    public void Duplicate()
    {
        Attempt(() =>
        {
            RequireCleanEditor();
            session.Load(ProjectCommands.Duplicate(Project));
            Refresh();
            Notice = "已复制为独立项目；请保存副本";
        });
    }

    public void ToggleArchive()
    {
        Attempt(() =>
        {
            RequireCleanEditor();
            Change(Project.Archived ? "解除归档" : "归档项目", p => p.Archived = !p.Archived, false, true);
        });
    }

    public void Undo()
    {
        Attempt(() => { RequireCleanEditor(); session.Undo(); Refresh(); }, "已撤销；累计审计保留");
    }

    public void Redo()
    {
        Attempt(() => { RequireCleanEditor(); session.Redo(); Refresh(); }, "已重做");
    }

    public void Calculate()
    {
        Attempt(() =>
        {
            RequireCleanEditor();
            Result = Engine.Run(Project);
            Notice = "计算完成；当前输入哈希 " + Result.InputHash[..12];
        });
    }

    public async Task RunScan()
    {
        if (Busy)
        {
            return;
        }
        Busy = true;
        Message("正在扫描约束内离散转速网格…");
        await Task.Yield();
        Attempt(() =>
        {
            RequireCleanEditor();
            Scan = SpeedScanner.Scan(Project, PlanId);
            Notice = Scan.Message;
            Error = !Scan.Feasible;
        });
        Busy = false;
        Error = Error || Scan is { Feasible: false };
        Notify();
    }

    public void AcceptScan()
    {
        Attempt(() =>
        {
            RequireCleanEditor();
            var scan = Scan ?? throw new InvalidOperationException("请先扫描");
            var id = "";
            Change("采纳扫描为新方案", p => id = ProjectCommands.ApplyScan(p, scan).Id);
            Select("plan", id, id);
        });
    }

    public void GenerateChecks()
    {
        Attempt(() =>
        {
            RequireCleanEditor();
            var result = Result ?? throw new InvalidOperationException("请先计算");
            var count = 0;
            Change("生成验证清单", p => count = ProjectCommands.GenerateChecks(p, result, PlanId), false);
            Notice = $"新增{count}条验证记录；已有组合不会重复添加";
        });
    }

    public void RunStudy()
    {
        Attempt(() =>
        {
            RequireCleanEditor();
            Study = RuleStudy.Run(Project, PlanId, StudyBands, StudyTolerances);
            Notice = $"规则敏感性试算完成，共{Study.Cases.Count}种组合；当前项目参数未改变";
        });
    }

    public void Save()
    {
        Attempt(() =>
        {
            RequireCleanEditor();
            var saved = session.Save(store);
            LastSavedPath = saved.Path;
            Notice = "已保存到 " + saved.Path + "；可从项目库重新打开";
        });
    }

    public void Open(string id, bool backup = false)
    {
        GuardReplace(() =>
        {
            var opened = store.Open(id, backup);
            session.Load(opened.Project, opened.Token, !backup);
            Kind = "project";
            ShowLibrary = false;
            Refresh();
            Notice = backup ? "已载入上次备份；请核对后保存" : "已从项目库重开；输入已重新计算";
        }, backup ? "恢复上次备份" : "打开已保存项目");
    }

    public void ImportProject(string text)
    {
        Attempt(() =>
        {
            var imported = ProjectCodec.Decode(text);
            GuardReplace(() =>
            {
                session.Load(ProjectCommands.Duplicate(imported));
                Kind = "project";
                Refresh();
                Notice = "导入已完整校验；作为独立副本打开，原始身份记录于审计";
            }, "打开导入项目的独立副本");
        });
    }

    public void PreviewCsv()
    {
        Attempt(() =>
        {
            RequireCleanEditor();
            Preview = CsvExchange.Preview(CsvText, CsvKind, Project);
            Notice = Preview.Valid ? "CSV预检通过；尚未修改项目" : "CSV有错误；按下方行号修复后重新预检";
        });
    }

    public void ApplyCsv()
    {
        Confirm("应用已预检CSV：挡位会新增一个方案；模态会追加到项目。", () =>
        {
            RequireCleanEditor();
            var preview = Preview ?? throw new InvalidOperationException("请先预检");
            if (!preview.Valid || preview.Kind != CsvKind || preview.InputHash != ProjectCodec.InputHash(Project))
            {
                throw new InvalidOperationException("预检无效或输入已变化；请重新预检");
            }
            var fresh = CsvExchange.Preview(CsvText, CsvKind, Project);
            if (!fresh.Valid || SerializePreview(preview) != SerializePreview(fresh))
            {
                throw new InvalidOperationException("CSV文本已变化；请重新预检");
            }
            Change("应用CSV", p =>
            {
                if (preview.Kind == "gears")
                {
                    var plan = new SpeedPlan { Name = "CSV导入方案", Gears = preview.Gears.ToList() };
                    p.Plans.Add(plan);
                    PlanId = plan.Id;
                }
                else
                {
                    p.Modes.AddRange(preview.Modes);
                }
            });
            if (preview.Kind == "gears")
            {
                Select("plan", PlanId, PlanId);
            }
            else
            {
                Select("mode", Project.Modes[^1].Id);
            }
        });
    }

    private static string SerializePreview(CsvPreview p)
    {
        return JsonSerializer.Serialize(new
        {
            Gears = p.Gears.Select(x => new { x.Name, x.Rpm, x.Fixed }),
            Modes = p.Modes.Select(x => new { x.Name, x.FrequencyHz, x.DampingRatio, x.Source })
        });
    }

    public void ToggleLibrary() { ShowLibrary = !ShowLibrary; Notify(); }
    public void ToggleExchange() { ShowExchange = !ShowExchange; Notify(); }
    public void ToggleHelp() { ShowHelp = !ShowHelp; Notify(); }

    public (string Content, string Mime, string Name) Export(string type)
    {
        RequireCleanEditor();
        if (type == "study")
        {
            var study = Study ?? throw new InvalidOperationException("先运行规则敏感性试算");
            return (RuleStudy.ToCsv(Project, study), "text/csv", "规则敏感性.csv");
        }
        if (type == "project")
        {
            return (ProjectCodec.Encode(Project), "application/json", Project.Name + ".blowerproj");
        }
        if (type == "checks")
        {
            return (Exports.Checks(Project), "text/csv", "验证清单.csv");
        }
        if (type == "gears")
        {
            return (Exports.GearTemplate(ActivePlan), "text/csv", "挡位模板.csv");
        }
        if (type == "modes")
        {
            return (Exports.ModeTemplate(Project), "text/csv", "模态模板.csv");
        }
        var result = Result ?? throw new InvalidOperationException("请完成有效输入并计算");
        return type switch
        {
            "contacts" => (Exports.Contacts(Project, result), "text/csv", "频率接近明细.csv"),
            "crossings" => (Exports.Crossings(Project, result), "text/csv", "交叉转速.csv"),
            "report" => (Exports.Report(Project, result, PlanId), "text/html", "鼓风机设计报告.html"),
            "svg" => (ChartModel.Create(Project, result, PlanId, ChartCeiling)
                .ToSvg(ActivePlan?.Name ?? "", Labels.Source(Project.Origin)), "image/svg+xml", "转速频率图.svg"),
            _ => throw new InvalidOperationException("未知导出类型")
        };
    }

    public void SetDownload(string url, string name)
    {
        ShowExchange = true;
        DownloadUrl = url;
        DownloadName = name;
        Message("导出已生成：" + name + "；点击下载，文件位于浏览器下载目录。");
    }
}
