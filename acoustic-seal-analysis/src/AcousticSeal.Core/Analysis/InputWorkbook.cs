using System.Globalization;
using System.Text;
using AcousticSeal.Core.Domain;

namespace AcousticSeal.Core.Analysis;

public sealed record WorkbookColumn(Guid TrialId, string Name, string Condition, SourceKind Source, string SourceNote);
public sealed record WorkbookCell(double? Level, bool Declared, string Note)
{
    public string Status => !Declared ? "未声明" : Level.HasValue ? "有效" : "明确缺失";
}
public sealed record WorkbookRow(double Frequency, List<WorkbookCell> Cells);

public sealed class InputWorkbookResult
{
    public List<WorkbookColumn> Columns { get; init; } = [];
    public List<WorkbookRow> Rows { get; init; } = [];
    public List<string> Warnings { get; init; } = [];
    public int CompleteRows => Rows.Count(row => row.Cells.All(cell => cell.Level.HasValue));
    public string Purpose { get; init; } = "原始频带人工核对，不自动配对、不计算封堵差、不证明跨列可比。";
}

public sealed class InputWorkbook
{
    public InputWorkbookResult Build(AcousticProject project, IReadOnlyList<Guid> ids, double? minimum, double? maximum)
    {
        if (ids.Count == 0 || ids.Count > 20 || ids.Distinct().Count() != ids.Count)
        {
            throw new DomainValidationException("workbook", "请显式选择1至20条不重复试验");
        }

        if (minimum > maximum || minimum is < 1 or > 100000 || maximum is < 1 or > 100000)
        {
            throw new DomainValidationException("workbook.frequency", "频带范围无效");
        }

        var trials = new List<AcousticTrial>();
        foreach (var id in ids)
        {
            trials.Add(project.Trials.FirstOrDefault(trial => trial.Id == id)
                ?? throw new DomainValidationException("workbook.trial", "选中的试验已不存在，请重新选择"));
        }

        var rows = new List<WorkbookRow>();
        foreach (var frequency in AnalysisEngine.Frequencies(trials).Where(frequency =>
            (!minimum.HasValue || frequency >= minimum) && (!maximum.HasValue || frequency <= maximum)))
        {
            var cells = new List<WorkbookCell>();
            foreach (var trial in trials)
            {
                var band = trial.Bands.FirstOrDefault(band => band.Frequency == frequency);
                cells.Add(new WorkbookCell(band?.Level, band is not null, band?.Note ?? ""));
            }

            rows.Add(new WorkbookRow(frequency, cells));
        }

        var warnings = new List<string>();
        if (trials.Select(trial => trial.Source).Distinct().Count() > 1)
        {
            warnings.Add("所选试验包含不同来源类别，只供原始输入核对，不能混合统计。");
        }

        var conditions = project.Conditions.ToDictionary(condition => condition.Id);
        var comparator = new Comparability();
        for (var index = 0; index < trials.Count; index++)
        {
            for (var other = index + 1; other < trials.Count; other++)
            {
                var reasons = comparator.Inspect(conditions[trials[index].ConditionId], conditions[trials[other].ConditionId], project.Settings);
                if (reasons.Count > 0)
                {
                    warnings.Add(trials[index].Name + " / " + trials[other].Name + "：" + string.Join("；", reasons));
                }
            }
        }

        return new InputWorkbookResult
        {
            Columns = trials.Select(trial => new WorkbookColumn(trial.Id, trial.Name,
                conditions[trial.ConditionId].Name, trial.Source, trial.SourceNote)).ToList(),
            Rows = rows,
            Warnings = warnings.Distinct().ToList()
        };
    }

    public byte[] Csv(InputWorkbookResult workbook)
    {
        var output = new StringBuilder("\uFEFF");
        var header = new List<string> { "frequency_hz" };
        foreach (var column in workbook.Columns)
        {
            header.Add(column.TrialId + "_level_db");
            header.Add(column.TrialId + "_state");
        }

        output.AppendLine(string.Join(",", header));
        foreach (var row in workbook.Rows)
        {
            var cells = new List<string> { row.Frequency.ToString("G17", CultureInfo.InvariantCulture) };
            foreach (var cell in row.Cells)
            {
                cells.Add(cell.Level?.ToString("G17", CultureInfo.InvariantCulture) ?? "");
                cells.Add(cell.Status);
            }

            output.AppendLine(string.Join(",", cells));
        }

        output.AppendLine();
        output.AppendLine("trial_id,name,condition,source_kind,source_note");
        foreach (var column in workbook.Columns)
        {
            output.AppendLine(string.Join(",", new[] { column.TrialId.ToString(), column.Name, column.Condition, column.Source.Text(), column.SourceNote }
                .Select(CsvCell)));
        }

        return Encoding.UTF8.GetBytes(output.ToString());
    }

    private static string CsvCell(string text)
    {
        if (text.Length > 0 && "=+-@\t\r".Contains(text[0]))
        {
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
