using MotorFrequency.Core.Domain;

namespace MotorFrequency.Core.Services;

public static class Demonstration
{
    public static Project Create()
    {
        var project = new Project
        {
            Name = "附件电机 · 60 Hz 候选核对（模拟）",
            Owner = "示例工程师",
            Purpose = "用固定模拟样例核对 1800 r/min 下转频 30 Hz、电气基频 60 Hz；验证候选同频不等于因果。",
            Demonstration = true,
            Motor = new MotorConfiguration
            {
                Name = "两极对十二槽无刷示例",
                PolePairs = 2,
                Slots = 12,
                MinimumRpm = 0,
                MaximumRpm = 6000,
                StepRpm = 100,
                InspectRpm = 1800,
                MaximumHz = 1500,
                IncludeSlotPass = true,
                IncludeIdealCogging = true,
                SymmetricUnskewedAssumption = true,
                AssumptionNotes = "仅示例：等槽、对称永磁、无偏心的理想周期，不作幅值预测。"
            }
        };
        project.CustomOrders.Add(new CustomOrder
        {
            Name = "二倍转频待验证",
            Order = 2,
            Basis = "模拟工程假设，与电气基频同频；通过变转速和电流/振动同步采集进一步区分。"
        });
        project.Bands.Add(new SensitiveBand
        {
            Name = "60 Hz 附近关注带",
            LowerHz = 55,
            UpperHz = 65,
            Origin = DataOrigin.Simulated,
            Basis = "人为设定的示例频段，不代表结构模态实测值。"
        });
        project.Bands.Add(new SensitiveBand
        {
            Name = "结构观察带（模拟）",
            LowerHz = 340,
            UpperHz = 380,
            Origin = DataOrigin.Simulated,
            Basis = "用于交叠区间计算示范，非实测结构模态。"
        });
        var spectrum = new Spectrum
        {
            Name = "1800 转模拟频谱",
            FileName = "内置确定性公式",
            Rpm = 1800,
            Unit = "dB",
            Reference = "模拟 dB 标度，无实际声学校准",
            Condition = "固定算式生成，非真实采集",
            Origin = DataOrigin.Simulated
        };
        for (var frequency = 0; frequency <= 1500; frequency += 2)
        {
            var background = 22 + 2 * Math.Sin(frequency * 0.071) + Math.Cos(frequency * 0.113);
            var peakA = 30 * Math.Exp(-Math.Pow((frequency - 60) / 5.0, 2));
            var peakB = 23 * Math.Exp(-Math.Pow((frequency - 360) / 8.0, 2));
            var peakC = 13 * Math.Exp(-Math.Pow((frequency - 120) / 7.0, 2));
            spectrum.Points.Add(new SpectrumPoint { FrequencyHz = frequency, Amplitude = background + peakA + peakB + peakC });
        }
        spectrum.InputSha256 = ProjectCodec.Hash(SpectrumCsv.Export(spectrum));
        project.Spectra.Add(spectrum);
        var baseline = ProjectCodec.Clone(project.Motor);
        baseline.PolePairs = 3;
        baseline.Name = "三极对方案（模拟）";
        project.Scenarios.Add(new Scenario
        {
            Name = "三极对参考（模拟）",
            Motor = baseline,
            CustomOrders = ProjectCodec.Clone(project.CustomOrders),
            Demonstration = true
        });
        project.History.Add(new AuditEvent { Action = "载入独立示例", Detail = "固定模拟公式；非实测；1800 r/min、p=2、Q=12" });
        return project;
    }
}
