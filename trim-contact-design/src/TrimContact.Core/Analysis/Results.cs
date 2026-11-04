namespace TrimContact.Core.Analysis;

public sealed class AnalysisReport
{
    public string Method { get; set; } = Domain.Product.Method;
    public string InputHash { get; set; } = "";
    public DateTimeOffset CalculatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ProjectName { get; set; } = "";
    public string Origin { get; set; } = "";
    public string Source { get; set; } = "";
    public int Phases { get; set; }
    public bool ToleranceEnabled { get; set; }
    public List<VariantResult> Variants { get; set; } = [];
}

public sealed class VariantResult
{
    public Guid VariantId { get; set; }
    public string Name { get; set; } = "";
    public int EvaluatedStates { get; set; }
    public int ToleranceCases { get; set; }
    public double MaxResidualN { get; set; }
    public List<PointEnvelope> Contacts { get; set; } = [];
    public List<PointEnvelope> Clips { get; set; } = [];
    public List<TraceResult> NominalTraces { get; set; } = [];
    public int SeparationPoints => Contacts.Count(c => c.MinCompressionMm <= Domain.Product.ContactEpsilon);
    public int SlidingPoints => Contacts.Count(c => c.MaxSlipMm > Domain.Product.ContactEpsilon);
    public int OverLimitPoints => Clips.Count(c => c.MaxUtilization > 1) + Contacts.Count(c => c.MaxUtilization > 1);
}

public sealed class PointEnvelope
{
    public Guid PointId { get; set; }
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public double MinCompressionMm { get; set; } = double.PositiveInfinity;
    public double MaxCompressionMm { get; set; }
    public double MinNormalN { get; set; } = double.PositiveInfinity;
    public double MaxNormalN { get; set; } = double.NegativeInfinity;
    public double MaxAbsShearN { get; set; }
    public double MaxSlipMm { get; set; }
    public double MaxUtilization { get; set; }
    public int DetachedSamples { get; set; }
    public int TotalSamples { get; set; }
    public int SlidingSamples { get; set; }
    public string GoverningCase { get; set; } = "";
    public string CompressionCase { get; set; } = "";
    public string SlipCase { get; set; } = "";
    public string State => Kind == "卡扣" ? (MaxUtilization > 1 ? "合力超限" : "未超用户限值")
        : MaxCompressionMm <= Domain.Product.ContactEpsilon ? "全采样脱离"
        : MinCompressionMm <= Domain.Product.ContactEpsilon ? "存在分离"
        : "持续接触";

    public void Accumulate(PointState s, string caseName, bool isClip)
    {
        if (s.CompressionMm < MinCompressionMm)
        {
            MinCompressionMm = s.CompressionMm;
            CompressionCase = caseName;
        }
        MaxCompressionMm = Math.Max(MaxCompressionMm, s.CompressionMm);
        MinNormalN = Math.Min(MinNormalN, s.NormalN);
        MaxNormalN = Math.Max(MaxNormalN, s.NormalN);
        MaxAbsShearN = Math.Max(MaxAbsShearN, Math.Abs(s.ShearN));
        if (s.SlipMm > MaxSlipMm)
        {
            MaxSlipMm = s.SlipMm;
            SlipCase = caseName;
        }
        if (s.Utilization >= MaxUtilization)
        {
            MaxUtilization = s.Utilization;
            GoverningCase = caseName;
        }
        if (!isClip && s.CompressionMm <= Domain.Product.ContactEpsilon)
            DetachedSamples++;
        if (s.SlipMm > Domain.Product.ContactEpsilon)
            SlidingSamples++;
        TotalSamples++;
    }
}

public sealed class TraceResult
{
    public Guid LoadId { get; set; }
    public string LoadName { get; set; } = "";
    public List<PhaseState> Samples { get; set; } = [];
}

public sealed record SweepRow(double Value, int SeparationPoints, int SlidingPoints,
    int OverLimitPoints, double MaxClipUtilization, double MaxCompressionMm, double MaxSlipMm);

public sealed record ComparisonRow(string Name, int SeparationPoints, int SlidingPoints,
    int OverLimitPoints, double MaxClipUtilization, double MaxSlipMm);
