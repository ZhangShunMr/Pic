using MotorFrequency.Core.Domain;
using MotorFrequency.Core.Services;

namespace MotorFrequency.App.ViewModels;

public sealed class SweepViewModel
{
    public string PolePairs { get; set; } = "1,2,3";
    public string Slots { get; set; } = "6,12,18";
    public string Segments { get; set; } = "8,10,12";
    public string ReferenceRpm { get; set; } = "1800";
    public string ObservedHz { get; set; } = "60";
    public string ToleranceHz { get; set; } = "3";
    public string InputHash { get; private set; } = "";
    public string Summary { get; private set; } = "尚未运行参数扫查。";
    public IReadOnlyList<SweepRow> Rows { get; private set; } = [];
    public IReadOnlyList<ReverseRow> ReverseRows { get; private set; } = [];
    public bool Error { get; private set; }
    public bool OnlyNearby { get; set; }
    public SweepRequest? LastRequest { get; private set; }

    public IReadOnlyList<SweepRow> DisplayRows => OnlyNearby ? Rows.Where(x => x.WithinTolerance).ToList() : Rows;

    public bool Stale(Project project)
    {
        return InputHash.Length > 0 && InputHash != ContextHash(project);
    }

    private static string ContextHash(Project project)
    {
        return ParameterSweep.ContextHash(project);
    }

    public void Run(Project project)
    {
        try
        {
            var request = new SweepRequest
            {
                ReferenceRpm = MotorDraft.Parse(ReferenceRpm, "参考转速"),
                ObservedHz = MotorDraft.Parse(ObservedHz, "观察频率"),
                ToleranceHz = MotorDraft.Parse(ToleranceHz, "容差")
            };
            if (project.Motor.Topology == MotorTopology.BrushlessPermanentMagnet)
            {
                request.PolePairs = ParameterSweep.ParseIntegers(PolePairs, "极对数组", 1, 64).ToList();
                request.Slots = ParameterSweep.ParseIntegers(Slots, "槽数组", 1, 512).ToList();
            }
            else
            {
                request.Segments = ParameterSweep.ParseIntegers(Segments, "换向片数组", 2, 512).ToList();
            }
            Rows = ParameterSweep.Calculate(project, request);
            ReverseRows = ParameterSweep.Reverse(project, request.ObservedHz, request.ToleranceHz);
            LastRequest = request;
            InputHash = ContextHash(project);
            Error = false;
            Summary = $"扫查完成：{Rows.Count} 条候选参考，{Rows.Count(x => x.WithinTolerance)} 条在 ±{Labels.Number(request.ToleranceHz)} Hz 内；不作可制造性或噪声优劣排序。";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            Rows = [];
            ReverseRows = [];
            LastRequest = null;
            InputHash = "";
            Error = true;
            Summary = ex.Message;
        }
    }

    public void Clear()
    {
        Rows = [];
        ReverseRows = [];
        InputHash = "";
        LastRequest = null;
        Error = false;
        Summary = "扫查结果已清除；项目输入没有改变。";
    }
}
