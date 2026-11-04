using System.Globalization;
using System.Text;
using TrimContact.Core.Analysis;
using TrimContact.Core.Domain;

namespace TrimContact.Core.IO;

public static class BalanceExport
{
    private static string Cell(string value)
    {
        if (value.Length > 0 && "=+-@".Contains(value[0])
            && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static string Number(double value) => value.ToString("G17", CultureInfo.InvariantCulture);

    private static void Add(StringBuilder text, params string[] values)
    {
        text.AppendLine(string.Join(",", values.Select(Cell)));
    }

    public static string Csv(DesignProject project, AnalysisReport report)
    {
        var rows = BalanceAudit.Evaluate(project, report);
        var text = new StringBuilder();
        Add(text, "软件", Product.Name, Product.Version, "输入SHA256", report.InputHash);
        Add(text, "来源", project.Origin.Label(), project.Source, "范围", "仅名义公差相位");
        Add(text, "限制", "仅法向力矩平衡；切向不包含扭转或高度偏心；弹性能不是异响能量");
        Add(text, "方案", "载荷", "相位deg", "外加法向N", "法向反力合计N", "外加切向N", "切向反力合计N",
            "法向差N", "切向差N", "X轴力矩差Nmm", "Y轴力矩差Nmm", "储存弹性能Nmm");
        foreach (var row in rows)
        {
            Add(text, row.Variant, row.Load, Number(row.PhaseDegrees), Number(row.AppliedNormalN),
                Number(row.ReactionNormalN), Number(row.AppliedShearN), Number(row.ReactionShearN),
                Number(row.NormalErrorN), Number(row.ShearErrorN), Number(row.MomentXErrorNmm),
                Number(row.MomentYErrorNmm), Number(row.ElasticEnergyNmm));
        }
        return text.ToString();
    }
}
