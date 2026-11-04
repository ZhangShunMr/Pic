using System.IO;
using Blower.Core.Domain;
using Blower.Core.IO;
using Blower.Core.Operations;
using Blower.Desktop.Services;

namespace Blower.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private string editorKind = "";
    private string editorId = "";

    private void BuildEditor()
    {
        draft = ProjectCodec.Clone(Project);
        editorKind = section;
        editorId = "";
        Form.Clear("属性编辑 · 应用后生效");
        switch (section)
        {
            case "project":
                Form.Text("项目名称", draft.Name, x => draft.Name = x);
                Form.Text("负责人", draft.Owner, x => draft.Owner = x);
                Form.Text("车辆 / 平台", draft.Vehicle, x => draft.Vehicle = x);
                Form.Source("来源性质", draft.Origin, x => draft.Origin = x);
                Form.Text("来源说明", draft.Source, x => draft.Source = x, "填写文件编号、工况、安装条件及来源单位");
                Form.Text("项目备注", draft.Notes, x => draft.Notes = x);
                break;
            case "settings":
                SettingsEditor(draft.Settings);
                break;
            case "orders" when selectedRow is Excitation order:
                editorId = order.Id;
                var o = draft.Orders.Single(x => x.Id == editorId);
                Form.Text("名称", o.Name, x => o.Name = x);
                Form.OrderType(o.Kind, x => o.Kind = x);
                Form.Number("阶次 / 倍数", o.Multiplier, x => o.Multiplier = x, "0.1～100；叶片倍频时乘以叶片数");
                Form.Boolean("启用", o.Enabled, x => o.Enabled = x);
                Form.Text("激励存在性依据", o.Evidence, x => o.Evidence = x);
                break;
            case "modes" when selectedRow is Mode mode:
                editorId = mode.Id;
                var m = draft.Modes.Single(x => x.Id == editorId);
                Form.Text("名称", m.Name, x => m.Name = x);
                Form.Number("模态频率 Hz", m.FrequencyHz, x => m.FrequencyHz = x, "0.1～50000Hz");
                Form.NullableNumber("阻尼比 ζ", m.DampingRatio, x => m.DampingRatio = x, "0～0.1；未知留空，0表示已知为零");
                Form.Number("附加半宽 Hz", m.ExtraBandHz, x => m.ExtraBandHz = x, "0～5000Hz；不与其他半宽相加");
                Form.Boolean("启用", m.Enabled, x => m.Enabled = x);
                Form.Source("来源性质", m.Origin, x => m.Origin = x);
                Form.Text("来源说明", m.Source, x => m.Source = x);
                Form.Text("结构位置 / 边界", m.Location, x => m.Location = x);
                break;
            case "plans" when selectedRow is Gear gear:
                editorKind = "gear";
                editorId = gear.Id;
                var g = draft.Plans.SelectMany(x => x.Gears).Single(x => x.Id == editorId);
                Form.Text("挡位名称", g.Name, x => g.Name = x);
                Form.Number("转速 rpm", g.Rpm, x => g.Rpm = x, "在分析范围内；挡位按转速递增并满足最小间距");
                Form.Boolean("扫描固定此挡", g.Fixed, x => g.Fixed = x);
                Form.Text("挡位备注", g.Notes, x => g.Notes = x);
                break;
            case "plans" when ActivePlan != null:
                editorKind = "plan";
                editorId = ActivePlan.Id;
                var p = draft.Plans.Single(x => x.Id == editorId);
                Form.Text("方案名称", p.Name, x => p.Name = x);
                Form.Text("方案备注", p.Notes, x => p.Notes = x);
                Form.Boolean("锁定方案", p.Locked, x => p.Locked = x, "锁定后禁止挡位修改；仍可复制为新方案");
                break;
            case "checks" when selectedRow is VerificationTask task:
                editorId = task.Id;
                var t = draft.Checks.Single(x => x.Id == editorId);
                Form.Text("验证任务", t.Title, x => t.Title = x);
                Form.Text("方法与工况", t.Method, x => t.Method = x);
                Form.Text("负责人", t.Owner, x => t.Owner = x);
                Form.CheckStatus(t.State, x => t.State = x);
                Form.Text("证据位置与结论", t.Evidence, x => t.Evidence = x, "写明记录编号、文件位置、测量条件与结论；软件不认证证据真实性");
                break;
        }
        Raise(nameof(HasEditor));
        Raise(nameof(Title));
    }

    private void SettingsEditor(DesignSettings s)
    {
        Form.Integer("叶片数", s.Blades, x => s.Blades = x, "1～200片");
        Form.Number("最低转速 rpm", s.MinRpm, x => s.MinRpm = x, "0～50000rpm，低于上限");
        Form.Number("最高转速 rpm", s.MaxRpm, x => s.MaxRpm = x, "1～50000rpm，高于下限");
        Form.Number("绝对半宽 Hz", s.AbsoluteBandHz, x => s.AbsoluteBandHz = x, "0～5000Hz；默认5Hz为可修改工程假设");
        Form.Number("相对半宽 %", s.RelativeBandPercent, x => s.RelativeBandPercent = x, "0～30%，默认3%");
        Form.Boolean("计入阻尼近似", s.UseDamping, x => s.UseDamping = x, "用 ζ×模态频率作为近似半宽；未知阻尼不计");
        Form.Number("转速容差 rpm", s.SpeedToleranceRpm, x => s.SpeedToleranceRpm = x, "0～1000rpm，默认±20rpm");
        Form.Number("最小挡位间距 rpm", s.MinimumGapRpm, x => s.MinimumGapRpm = x, "1～5000rpm；适用于全部方案");
        Form.Number("扫描步长 rpm", s.ScanStepRpm, x => s.ScanStepRpm = x, "1～500rpm，网格以0rpm为原点");
        Form.Number("扫描窗口 ±rpm", s.ScanWindowRpm, x => s.ScanWindowRpm = x, "0～3000rpm；不超出分析范围");
        Form.Number("偏移权重", s.ShiftWeight, x => s.ShiftWeight = x, "0～1；局部代价=接近组合数+权重×归一化偏移");
    }

    private void ApplyEditor()
    {
        if (!Form.Changed)
        {
            Message("属性没有变化。");
            return;
        }
        if (Project.Archived)
        {
            throw new InvalidOperationException("项目已归档，请先解除归档");
        }
        if (editorKind == "gear" && Project.Plans.Single(x => x.Gears.Any(g => g.Id == editorId)).Locked)
        {
            throw new InvalidOperationException("方案已锁定；取消选中挡位，解除方案锁定后再编辑");
        }
        Form.Apply();
        Validation.RequireValid(draft);
        var edited = ProjectCodec.Clone(draft);
        var id = editorId;
        var kind = editorKind;
        Change("编辑" + SectionTitle, p =>
        {
            switch (kind)
            {
                case "project":
                    p.Name = edited.Name;
                    p.Owner = edited.Owner;
                    p.Vehicle = edited.Vehicle;
                    p.Origin = edited.Origin;
                    p.Source = edited.Source;
                    p.Notes = edited.Notes;
                    break;
                case "settings":
                    p.Settings = edited.Settings;
                    break;
                case "orders":
                    p.Orders[p.Orders.FindIndex(x => x.Id == id)] = edited.Orders.Single(x => x.Id == id);
                    break;
                case "modes":
                    p.Modes[p.Modes.FindIndex(x => x.Id == id)] = edited.Modes.Single(x => x.Id == id);
                    break;
                case "gear":
                    var plan = p.Plans.Single(x => x.Gears.Any(g => g.Id == id));
                    plan.Gears[plan.Gears.FindIndex(x => x.Id == id)] = edited.Plans.SelectMany(x => x.Gears).Single(x => x.Id == id);
                    break;
                case "plan":
                    var target = p.Plans.Single(x => x.Id == id);
                    var source = edited.Plans.Single(x => x.Id == id);
                    target.Name = source.Name;
                    target.Notes = source.Notes;
                    target.Locked = source.Locked;
                    break;
                case "checks":
                    var check = edited.Checks.Single(x => x.Id == id);
                    check.Updated = DateTimeOffset.UtcNow;
                    p.Checks[p.Checks.FindIndex(x => x.Id == id)] = check;
                    break;
            }
        }, kind is not "project" and not "checks" and not "plan");
    }

    private bool LeaveEditor()
    {
        if (!Form.Changed)
        {
            return true;
        }
        var decision = dialogs.Unsaved("当前属性有未应用修改。是：校验并应用；否：放弃属性修改；取消：继续编辑。");
        if (decision == SaveDecision.Cancel)
        {
            return false;
        }
        if (decision == SaveDecision.Save)
        {
            try
            {
                ApplyEditor();
            }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException)
            {
                Message(e.Message, true);
                return false;
            }
        }
        else
        {
            BuildEditor();
        }
        return true;
    }

    private void ResetEditor()
    {
        if (!Form.Changed || dialogs.Confirm("放弃尚未应用的属性修改？"))
        {
            BuildEditor();
        }
    }

    public bool CanClose()
    {
        if (busy)
        {
            Message("计算正在进行，请等待结束再关闭。", true);
            return false;
        }
        if (!LeaveEditor())
        {
            return false;
        }
        if (!Dirty)
        {
            return true;
        }
        return dialogs.Unsaved("项目有未保存修改。是：保存项目；否：放弃修改；取消：返回工作台。") switch
        {
            SaveDecision.Save => Save(false),
            SaveDecision.Discard => true,
            _ => false
        };
    }

    private void New()
    {
        if (CanClose())
        {
            Load(new Project());
            Message("已建立空白项目。示例通过独立入口载入。");
        }
    }

    private void Open()
    {
        if (!CanClose())
        {
            return;
        }
        var path = dialogs.OpenProject();
        if (path == null)
        {
            return;
        }
        var opened = DesktopFiles.Open(path);
        Load(opened.Project, opened.Path, opened.Token, true);
        Message("已打开并校验：" + path);
    }

    private void LoadDemo()
    {
        if (CanClose())
        {
            Load(Demo.Create());
            Message("已载入独立模拟示例；来源、界面和导出持续标明模拟数据。");
        }
    }

    private void DuplicateProject()
    {
        if (LeaveEditor())
        {
            var copy = ProjectCommands.Duplicate(Project);
            if (CanClose())
            {
                Load(copy);
                Message("已建立独立副本，原文件保持原修订。");
            }
        }
    }

    private void Load(Project project, string path = "", string token = "", bool saved = false)
    {
        session.Load(project, saved: saved);
        filePath = path;
        diskToken = token;
        savedContent = saved ? ProjectCodec.Encode(Project) : "";
        selectedRow = null;
        activePlan = null;
        Result = null;
        Scan = null;
        Study = null;
        Comparison = null;
        Preview = null;
        previewText = "";
        Rebuild();
    }

    private bool Save(bool saveAs)
    {
        try
        {
            if (!LeaveEditor())
            {
                return false;
            }
            var path = saveAs || string.IsNullOrEmpty(filePath) ? dialogs.SaveProject(Project.Name) : filePath;
            if (path == null)
            {
                return false;
            }
            var sameFile = string.Equals(Path.GetFullPath(path), filePath, StringComparison.OrdinalIgnoreCase);
            var token = DesktopFiles.Save(Project, path, sameFile ? diskToken : null);
            filePath = Path.GetFullPath(path);
            diskToken = token;
            savedContent = ProjectCodec.Encode(Project);
            Rebuild();
            Message("保存成功，已重开核对：" + filePath);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Message("保存失败：" + e.Message, true);
            return false;
        }
    }

    private void History(bool redo)
    {
        if (!LeaveEditor())
        {
            return;
        }
        if (redo)
        {
            session.Redo();
        }
        else
        {
            session.Undo();
        }
        selectedRow = null;
        Result = null;
        Scan = null;
        Study = null;
        Comparison = null;
        Rebuild();
        Message(redo ? "已重做；累计审计保留。" : "已撤销；累计审计保留。");
    }

    private void ToggleArchive()
    {
        if (LeaveEditor() && dialogs.Confirm(Project.Archived ? "解除项目归档以继续编辑？" : "归档项目将禁止编辑；仍可打开、计算和导出。继续？"))
        {
            Change(Project.Archived ? "解除归档" : "归档", p => p.Archived = !p.Archived, false, true);
        }
    }

    private void Add()
    {
        if (!LeaveEditor())
        {
            return;
        }
        switch (section)
        {
            case "orders":
                Change("新增阶次", p =>
                {
                    var used = p.Orders.Where(x => x.Enabled).Select(x => x.EffectiveOrder(p.Settings.Blades)).ToHashSet();
                    var q = Enumerable.Range(1, 100).FirstOrDefault(x => !used.Contains(x));
                    p.Orders.Add(new() { Multiplier = q == 0 ? 1 : q, Enabled = q != 0, Name = "轴阶次 " + q });
                });
                break;
            case "modes":
                Change("新增模态", p => p.Modes.Add(new() { Origin = p.Origin }));
                break;
            case "plans":
                if (ActivePlan == null || dialogs.Confirm("是：新增独立方案；否：向当前方案增加挡位。"))
                {
                    Change("新增方案", p => p.Plans.Add(new() { Name = "方案 " + (p.Plans.Count + 1) }));
                }
                else
                {
                    var id = ActivePlan.Id;
                    Change("新增挡位", p =>
                    {
                        var plan = p.Plans.Single(x => x.Id == id);
                        if (plan.Locked)
                        {
                            throw new InvalidOperationException("方案已锁定");
                        }
                        var rpm = plan.Gears.Count == 0 ? p.Settings.MinRpm : plan.Gears[^1].Rpm + p.Settings.MinimumGapRpm;
                        plan.Gears.Add(new() { Name = "挡位 " + (plan.Gears.Count + 1), Rpm = rpm });
                    });
                }
                break;
            default:
                Message("请在阶次、模态或挡位方案中新增对象。", true);
                break;
        }
    }

    private void Delete()
    {
        if (!LeaveEditor())
        {
            return;
        }
        var kind = selectedRow switch
        {
            Excitation => "order",
            Mode => "mode",
            Gear => "gear",
            VerificationTask => "check",
            _ => section == "plans" && ActivePlan != null ? "plan" : ""
        };
        var id = selectedRow switch
        {
            Excitation x => x.Id,
            Mode x => x.Id,
            Gear x => x.Id,
            VerificationTask x => x.Id,
            _ => ActivePlan?.Id ?? ""
        };
        if (kind.Length == 0)
        {
            Message("先选中需要删除的对象；删除方案时先取消挡位选择。", true);
            return;
        }
        if (dialogs.Confirm("删除所选对象？关联验证记录存在时会阻止删除，可撤销此操作。"))
        {
            Change("删除对象", p => ProjectCommands.RemoveObject(p, kind, id), kind != "check");
        }
    }

    private void CopyPlan()
    {
        if (LeaveEditor() && ActivePlan != null)
        {
            var id = ActivePlan.Id;
            Change("复制方案", p =>
            {
                var source = p.Plans.Single(x => x.Id == id);
                p.Plans.Add(ProjectCommands.CopyPlan(source, source.Name + " · 副本"));
            });
        }
    }
}
