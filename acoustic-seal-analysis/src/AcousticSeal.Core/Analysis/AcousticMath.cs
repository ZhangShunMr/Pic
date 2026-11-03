namespace AcousticSeal.Core.Analysis;

public static class AcousticMath
{
    private static readonly double[] Critical95 =
    [
        12.706204736, 4.302652730, 3.182446305, 2.776445105, 2.570581836,
        2.446911851, 2.364624252, 2.306004135, 2.262157163, 2.228138852,
        2.200985160, 2.178812830, 2.160368656, 2.144786688, 2.131449546,
        2.119905299, 2.109815578, 2.100922040, 2.093024054, 2.085963447,
        2.079613845, 2.073873068, 2.068657610, 2.063898562, 2.059538553,
        2.055529439, 2.051830516, 2.048407142, 2.045229642, 2.042272456
    ];

    public static double? EnergyMean(IEnumerable<double> input)
    {
        var values = Checked(input);
        if (values.Count == 0)
        {
            return null;
        }

        var maximum = values.Max();
        var sum = CompensatedSum(values.Select(value => Math.Pow(10, (value - maximum) / 10)));
        return maximum + 10 * Math.Log10(sum / values.Count);
    }

    public static double? EnergySum(IEnumerable<double> input)
    {
        var values = Checked(input);
        if (values.Count == 0)
        {
            return null;
        }

        var maximum = values.Max();
        var sum = CompensatedSum(values.Select(value => Math.Pow(10, (value - maximum) / 10)));
        return maximum + 10 * Math.Log10(sum);
    }

    public static double? Mean(IEnumerable<double> input)
    {
        var values = Checked(input);
        return values.Count == 0 ? null : CompensatedSum(values) / values.Count;
    }

    public static double? SampleStandardDeviation(IEnumerable<double> input)
    {
        var values = Checked(input);
        if (values.Count < 2)
        {
            return null;
        }

        var mean = Mean(values)!.Value;
        var sum = CompensatedSum(values.Select(value => (value - mean) * (value - mean)));
        return Math.Sqrt(sum / (values.Count - 1));
    }

    public static DescriptiveStatistics Describe(IEnumerable<double> input)
    {
        var values = Checked(input);
        if (values.Count == 0)
        {
            return new DescriptiveStatistics();
        }

        return new DescriptiveStatistics
        {
            Count = values.Count,
            ArithmeticMean = Mean(values),
            EnergyMean = EnergyMean(values),
            StandardDeviation = SampleStandardDeviation(values),
            Minimum = values.Min(),
            Maximum = values.Max(),
            Range = values.Max() - values.Min()
        };
    }

    public static PairedStatistics Paired(
        IReadOnlyList<double> baseline,
        IReadOnlyList<double> target,
        bool independent)
    {
        if (baseline.Count != target.Count)
        {
            throw new ArgumentException("配对基线与目标数量须相同");
        }

        var differences = baseline.Zip(target, (before, after) => before - after).ToList();
        var mean = Mean(differences);
        var deviation = SampleStandardDeviation(differences);
        double? lower = null;
        double? upper = null;
        var note = "n<2，不生成置信区间";
        if (differences.Count > 1 && !independent)
        {
            note = "同组配对复用试验，独立性不满足，不生成置信区间";
        }
        else if (differences.Count > 1)
        {
            var freedom = differences.Count - 1;
            var critical = Critical95[Math.Min(freedom, 30) - 1];
            var half = critical * deviation!.Value / Math.Sqrt(differences.Count);
            lower = mean - half;
            upper = mean + half;
            note = "独立配对差近似正态的双侧95% Student t均值区间；" +
                (freedom > 30 ? "df>30保守使用df=30" : "df=" + freedom) +
                "；不含仪器系统不确定性";
        }

        return new PairedStatistics
        {
            Count = differences.Count,
            MeanDifference = mean,
            StandardDeviation = deviation,
            GroupEnergyMeanDifference = baseline.Count == 0 ? null : EnergyMean(baseline) - EnergyMean(target),
            Lower95 = lower,
            Upper95 = upper,
            IntervalNote = note
        };
    }

    private static List<double> Checked(IEnumerable<double> values)
    {
        var list = values.ToList();
        if (list.Any(value => !double.IsFinite(value)))
        {
            throw new ArgumentException("计算输入须为有限数值；缺失值应在明确口径后筛选");
        }

        return list;
    }

    private static double CompensatedSum(IEnumerable<double> values)
    {
        double total = 0;
        double correction = 0;
        foreach (var value in values)
        {
            var adjusted = value - correction;
            var next = total + adjusted;
            correction = (next - total) - adjusted;
            total = next;
        }

        return total;
    }
}
