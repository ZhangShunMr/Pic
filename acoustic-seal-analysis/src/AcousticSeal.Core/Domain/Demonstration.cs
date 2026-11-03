namespace AcousticSeal.Core.Domain;

public static class Demonstration
{
    public static Guid StableId(int number)
    {
        return Guid.Parse("00000000-0000-4000-8000-" + number.ToString("D12"));
    }

    public static AcousticProject Create()
    {
        var project = new AcousticProject
        {
            Id = StableId(1),
            Name = "模拟演示 · 车门密封与前围孔封堵",
            Owner = "演示操作员（虚构）",
            VehicleProgramme = "SIM-ONLY",
            Objective = "验证工作流与数值；不证明真实车辆降噪效果",
            Description = "全部数值为固定模拟数列，来源：Demonstration.cs。无真实车辆、采集或登记事实。",
            CreatedAt = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(2026, 1, 1, 9, 0, 0, TimeSpan.Zero)
        };
        project.Locations =
        [
            new SealLocation
            {
                Id = StableId(10),
                Name = "左前门上沿密封（模拟）",
                Zone = "左前门",
                Material = "模拟胶带",
                Extent = "模拟上沿范围A至B",
                Procedure = "模拟记录：连续覆盖间隙，记录覆盖起止位置。",
                RecoveryProcedure = "模拟记录：移除胶带后复核原有边界。",
                Reference = "SIM-LOCATION-01",
                Notes = "不构成真实车辆作业指导。"
            },
            new SealLocation
            {
                Id = StableId(11),
                Name = "前围线束孔（模拟）",
                Zone = "前围",
                Material = "模拟密封泥",
                Extent = "模拟孔周边间隙",
                Procedure = "模拟记录：覆盖孔周围间隙。",
                RecoveryProcedure = "模拟记录：移除临时材料。",
                Reference = "SIM-LOCATION-02",
                Notes = "全部为模拟对象。"
            }
        ];
        project.Conditions.Add(new MeasurementCondition
        {
            Id = StableId(20),
            Name = "模拟静态宽带激励",
            Vehicle = "SIM-001",
            Microphone = "模拟驾驶员耳旁固定测点",
            Excitation = "模拟宽带外部声源",
            Surface = "模拟半消声室",
            Gear = "P",
            Weighting = "Z",
            TimeDefinition = "Leq",
            BandType = "octave",
            Instrument = "SIM-METER",
            Calibration = "模拟标定记录，只验证口径字段",
            Speed = 0,
            Rpm = 0,
            Temperature = 23,
            Duration = 30,
            SampleRate = 48000,
            Synchronization = "模拟共用时钟",
            NonOverlappingBands = true,
            Notes = "所有条件值为模拟，不表示仪器已真实标定。"
        });
        AddTrial(project, 30, "基线1", TrialKind.Baseline, [], "baseline", [70, 68, 66], 0);
        AddTrial(project, 31, "基线2", TrialKind.Baseline, [], "baseline", [70.2, 68.1, 66.2], 1);
        AddTrial(project, 32, "门密封单项1", TrialKind.Single, [StableId(10)], "door", [67, 66, 65], 2);
        AddTrial(project, 33, "门密封单项2", TrialKind.Single, [StableId(10)], "door", [67.1, 66.2, 65.1], 3);
        AddTrial(project, 34, "线束孔单项", TrialKind.Single, [StableId(11)], "", [68, 66.5, 65.5], 4);
        AddTrial(project, 35, "两位置组合测量（模拟）", TrialKind.Combination, [StableId(10), StableId(11)], "", [65.5, 64.5, 64], 5);
        AddTrial(project, 36, "结束基线", TrialKind.Baseline, [], "baseline", [70.4, 68.2, 66.3], 6);
        AddTrial(project, 37, "解除封堵恢复", TrialKind.Recovery, [], "", [70.1, 68.2, 66.1], 7);
        AddPair(project, 40, 30, 32, PairPurpose.Effect);
        AddPair(project, 41, 31, 33, PairPurpose.Effect);
        AddPair(project, 42, 30, 34, PairPurpose.Effect);
        AddPair(project, 43, 30, 35, PairPurpose.Effect);
        AddPair(project, 44, 30, 36, PairPurpose.Drift);
        AddPair(project, 45, 30, 37, PairPurpose.Recovery);
        new ProjectValidator().Ensure(project);
        return project;
    }

    private static void AddTrial(AcousticProject project, int id, string name, TrialKind kind,
        List<Guid> locations, string repeat, double[] levels, int minute)
    {
        project.Trials.Add(new AcousticTrial
        {
            Id = StableId(id),
            Name = name,
            Kind = kind,
            ConditionId = StableId(20),
            LocationIds = locations,
            Timestamp = new DateTimeOffset(2026, 1, 1, 10, minute, 0, TimeSpan.Zero),
            RepeatGroup = repeat,
            Source = SourceKind.Simulation,
            SourceNote = "固定模拟数列；AcousticSeal.Core/Domain/Demonstration.cs；非实测。",
            OriginalFile = "SIM-NO-REAL-FILE",
            Operator = "模拟操作员",
            Preparation = "模拟准备记录，不表示真实试验已执行。",
            Notes = "仅用于软件流程和数值核验。",
            Bands = new[] { 125.0, 250.0, 500.0 }.Zip(levels, (frequency, level) =>
                new BandMeasurement { Frequency = frequency, Level = level }).ToList()
        });
    }

    private static void AddPair(AcousticProject project, int id, int baseline, int target, PairPurpose purpose)
    {
        project.Pairs.Add(new TrialPair
        {
            Id = StableId(id),
            BaselineId = StableId(baseline),
            TargetId = StableId(target),
            Purpose = purpose,
            Notes = "模拟案例显式指定，不依赖名称或时间自动配对。"
        });
    }
}
