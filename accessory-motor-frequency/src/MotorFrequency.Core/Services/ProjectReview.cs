using MotorFrequency.Core.Domain;

namespace MotorFrequency.Core.Services;

public sealed record ReviewItem(string Subject, string State, string Evidence, string NextAction);

public static class ProjectReview
{
    public static IReadOnlyList<ReviewItem> Inspect(Project project)
    {
        var result = new List<ReviewItem>();
        result.Add(new("项目档案", string.IsNullOrWhiteSpace(project.Purpose) ? "待补充" : "已填写", "项目目的：" + project.Purpose, "确认负责人和本次要区分的频率来源；不自动认证身份。"));
        var motor = project.Motor;
        var candidates = FrequencyEngine.Candidates(motor, project.CustomOrders);
        result.Add(new("电机拓扑", "已限定", Labels.Topology(motor.Topology), motor.Topology == MotorTopology.BrushlessPermanentMagnet ? "确认同步运行；异步电机及滑差模型不适用。" : "确认永磁有刷直流；单刷片通过不是全机换向事件数。"));
        result.Add(new("采样转速", "可复核", $"{FrequencyEngine.Speeds(motor).Count} 个转速点；终点纳入；观察 {Labels.Number(motor.InspectRpm)} r/min", "CSV 扫描按步长，直线和交叠按解析公式，不混用采样精度。"));
        result.Add(new("候选清单", "可复核", $"共 {candidates.Count} 个候选，显示 {candidates.Count(x => x.Visible)} 个", "隐藏项不进入图、频率扫描、交叠和匹配；报告仍列出其依据。"));
        if (motor.IncludeIdealCogging && motor.Topology == MotorTopology.BrushlessPermanentMagnet)
        {
            result.Add(new("理想齿槽假设", "用户确认", motor.AssumptionNotes, "需要电磁模型或试验确认周期；软件未计算空间力波或幅值。"));
        }
        foreach (var band in project.Bands)
        {
            result.Add(new("频段：" + band.Name, band.Enabled ? "已启用" : "已停用", $"{Labels.Number(band.LowerHz)}～{Labels.Number(band.UpperHz)} Hz；{Labels.Origin(band.Origin)}；{band.Basis}", "核对模态/敏感区来源与边界条件，频率交叠不证明共振。"));
        }
        foreach (var spectrum in project.Spectra)
        {
            var total = spectrum.Points.Count;
            var inside = spectrum.Points.Count(x => x.FrequencyHz <= motor.MaximumHz);
            var inputHash = spectrum.InputSha256;
            var state = spectrum.Origin == DataOrigin.MeasuredDeclared ? "用户声明实测" : Labels.Origin(spectrum.Origin);
            result.Add(new("频谱：" + spectrum.Name, state, $"{total} 个原始点，{inside} 点在图表范围；{Labels.Number(spectrum.Rpm)} r/min；单位 {spectrum.Unit}；参考 {spectrum.Reference}", "确认幅值是频谱值而非时域样本，转速与实际采集一致。"));
            result.Add(new("输入追溯：" + spectrum.Name, inputHash.Length == 64 ? "已记录哈希" : "待补充", "文件 " + spectrum.FileName + "；SHA-256 " + inputHash, "哈希用于识别导入文本，不认证采集设备或校准。原始频点内嵌保存。"));
        }
        if (project.Spectra.Count == 0)
        {
            result.Add(new("频谱证据", "未提供", "当前仅有参数候选", "可继续参数研究；判断实际激励需另行导入相应工况频谱并同步验证。"));
        }
        foreach (var scenario in project.Scenarios)
        {
            var different = scenario.Motor.Topology != motor.Topology;
            result.Add(new("快照：" + scenario.Name, different ? "拓扑不同" : "同拓扑参考", $"快照 p={scenario.Motor.PolePairs}，Q={scenario.Motor.Slots}，K={scenario.Motor.CommutatorSegments}；当前观察 {Labels.Number(motor.InspectRpm)} r/min", "比较只给同一转速下频率差，不比较声级、效率或制造可行性。"));
        }
        result.Add(new("输出边界", "持续保留", Software.Boundary, "报告和项目配套保存；原始点 CSV 不单独携带工况，需保留元信息。"));
        return result;
    }

    public static string Json(Project project)
    {
        var output = new
        {
            software = Software.Name,
            version = Software.Version,
            projectId = project.Id,
            projectName = project.Name,
            source = project.Demonstration ? "模拟演示" : "人工参数及用户资料",
            generatedAt = DateTimeOffset.UtcNow,
            inputSha256 = ProjectCodec.Hash(ProjectCodec.Serialize(project)),
            items = Inspect(project),
            boundary = Software.Boundary
        };
        return System.Text.Json.JsonSerializer.Serialize(output, ProjectCodec.Options);
    }
}
