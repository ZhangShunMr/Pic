using System.IO;
using Blower.Core.Domain;
using Blower.Core.IO;
using Blower.Desktop.Services;

namespace Blower.Desktop.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private ProjectStore? library;
    private string libraryToken = "";
    private string libraryId = "";
    public string LibraryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BlowerResonance", "Projects");
    private ProjectStore Library => library ??= new ProjectStore(LibraryPath);

    private void SaveLibrary()
    {
        if (!LeaveEditor())
        {
            return;
        }
        var saved = Library.Save(Project, libraryId == Project.Id ? libraryToken : "");
        libraryToken = saved.Token;
        libraryId = Project.Id;
        filePath = saved.Path;
        diskToken = saved.Token;
        savedContent = ProjectCodec.Encode(Project);
        Rebuild();
        Message("项目库保存成功：" + saved.Path);
    }

    private void OpenLibrary(bool backup)
    {
        if (SelectedRow is not StoredProject selected)
        {
            throw new InvalidOperationException("先在本地项目库选中项目");
        }
        if (!CanClose())
        {
            return;
        }
        var opened = Library.Open(selected.Id, backup);
        if (backup)
        {
            Load(opened.Project);
            libraryId = selected.Id;
            libraryToken = opened.Token;
            Message("已从真实备份恢复到未保存草稿；原项目仍在磁盘，核对后另存或保存到项目库。");
        }
        else
        {
            Load(opened.Project, opened.Path, opened.Token, true);
            libraryId = selected.Id;
            libraryToken = opened.Token;
            Message("已打开本地项目库项目：" + opened.Path);
        }
    }

    private void AddPlan()
    {
        if (!LeaveEditor())
        {
            return;
        }
        var newId = Software.Id();
        Change("新增方案", p => p.Plans.Add(new() { Id = newId, Name = "方案 " + (p.Plans.Count + 1) }));
        ActivePlan = Project.Plans.Single(x => x.Id == newId);
    }

    private void AddGear()
    {
        if (!LeaveEditor())
        {
            return;
        }
        var id = ActivePlan?.Id ?? throw new InvalidOperationException("先新增或选择方案");
        Change("新增挡位", p =>
        {
            var plan = p.Plans.Single(x => x.Id == id);
            if (plan.Locked)
            {
                throw new InvalidOperationException("方案已锁定，请在方案属性解除锁定");
            }
            var rpm = plan.Gears.Count == 0 ? p.Settings.MinRpm : plan.Gears[^1].Rpm + p.Settings.MinimumGapRpm;
            plan.Gears.Add(new() { Name = "挡位 " + (plan.Gears.Count + 1), Rpm = rpm });
        });
    }
}
