using System.Globalization;
using System.Text;
using AcousticSeal.Core.Domain;
using AcousticSeal.Core.Operations;

namespace AcousticSeal.Core.IO;

public sealed record CsvRecord(int Line, List<string> Cells);
public sealed record ImportChange(Guid TrialId, string TrialName, int Added, int Replaced, int Missing, List<BandMeasurement> Bands);

public sealed class ImportPreview
{
    public string ProjectHash { get; init; } = "";
    public ImportMode Mode { get; init; }
    public char Delimiter { get; init; }
    public int Rows { get; init; }
    public List<ValidationMessage> Problems { get; init; } = [];
    public List<ImportChange> Changes { get; init; } = [];
    public bool CanApply => Problems.Count == 0 && Rows > 0 && Changes.Count > 0;
    public int Missing => Changes.Sum(change => change.Missing);
    public int Replaced => Changes.Sum(change => change.Replaced);
}

public sealed class CsvImportService
{
    public ImportPreview Preview(AcousticProject project, string content, ImportMode mode, char delimiter = ',')
    {
        if (delimiter is not (',' or ';' or '\t'))
        {
            throw new DomainValidationException("delimiter", "仅支持逗号、分号或制表符");
        }

        if (Encoding.UTF8.GetByteCount(content) > 5 * 1024 * 1024)
        {
            throw new DomainValidationException("csv", "CSV不得超过5MiB");
        }

        var records = CsvParser.Read(content.TrimStart('\uFEFF'), delimiter);
        var problems = new List<ValidationMessage>();
        var expected = new[] { "trial_id", "frequency_hz", "level_db" };
        if (records.Count == 0 || !records[0].Cells.SequenceEqual(expected))
        {
            throw new DomainValidationException("csv.header", "表头必须为trial_id、frequency_hz、level_db三列，分隔符须与选择一致");
        }

        if (records.Count > 100001)
        {
            throw new DomainValidationException("csv", "最多允许100000数据行");
        }

        var trials = project.Trials.ToDictionary(trial => trial.Id);
        var updates = new Dictionary<Guid, List<BandMeasurement>>();
        var seen = new HashSet<(Guid, double)>();
        foreach (var record in records.Skip(1))
        {
            var path = "CSV第" + record.Line + "行";
            if (record.Cells.Count != 3)
            {
                problems.Add(new(path, "列数不是3"));
                continue;
            }

            if (!Guid.TryParse(record.Cells[0].Trim(), out var id) || !trials.ContainsKey(id))
            {
                problems.Add(new(path + ".trial_id", "试验标识不存在；先建立试验并使用其GUID"));
                continue;
            }

            if (!double.TryParse(record.Cells[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var frequency) ||
                !double.IsFinite(frequency) || frequency < 1 || frequency > 100000)
            {
                problems.Add(new(path + ".frequency_hz", "频率须为1至100000Hz有限数值"));
                continue;
            }

            if (!seen.Add((id, frequency)))
            {
                problems.Add(new(path + ".frequency_hz", "同一试验频率重复，整批不可应用"));
                continue;
            }

            var text = record.Cells[2].Trim();
            double? level = null;
            if (text is not ("" or "NA" or "N/A" or "null"))
            {
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ||
                    !double.IsFinite(value) || value < -100 || value > 200)
                {
                    problems.Add(new(path + ".level_db", "声级须为-100至200dB，或空白/NA/N/A/null"));
                    continue;
                }

                level = value;
            }

            if (!updates.TryGetValue(id, out var bands))
            {
                bands = [];
                updates[id] = bands;
            }

            bands.Add(new BandMeasurement { Frequency = frequency, Level = level });
        }

        var changes = new List<ImportChange>();
        foreach (var (id, bands) in updates)
        {
            var trial = trials[id];
            var original = trial.Bands.Select(band => band.Frequency).ToHashSet();
            var resultCount = mode == ImportMode.Replace ? bands.Count : original.Union(bands.Select(band => band.Frequency)).Count();
            if (resultCount > Software.MaximumBands)
            {
                problems.Add(new("csv." + trial.Name, "应用后超过每试验200频带"));
            }

            changes.Add(new ImportChange(id, trial.Name, bands.Count(band => !original.Contains(band.Frequency)),
                bands.Count(band => original.Contains(band.Frequency)), bands.Count(band => !band.Level.HasValue), bands));
        }

        if (records.Count == 1)
        {
            problems.Add(new("csv", "没有数据行"));
        }

        return new ImportPreview
        {
            ProjectHash = ProjectCodec.Fingerprint(project),
            Mode = mode,
            Delimiter = delimiter,
            Rows = records.Count - 1,
            Problems = problems,
            Changes = changes
        };
    }

    public void Apply(ProjectSession session, ImportPreview preview)
    {
        if (!preview.CanApply)
        {
            throw new DomainValidationException("csv", "预检未通过，数据没有改变");
        }

        if (ProjectCodec.Fingerprint(session.Project) != preview.ProjectHash)
        {
            throw new DomainValidationException("csv", "预检后项目已改变，请重新预检，禁止应用过期结果");
        }

        session.Apply("CSV整体导入：" + preview.Mode, session.Project.Id.ToString(), project =>
        {
            foreach (var change in preview.Changes)
            {
                var trial = project.Trials.Single(trial => trial.Id == change.TrialId);
                if (preview.Mode == ImportMode.Replace)
                {
                    trial.Bands = ProjectCodec.Clone(change.Bands);
                }
                else
                {
                    var updated = change.Bands.Select(band => band.Frequency).ToHashSet();
                    trial.Bands.RemoveAll(band => updated.Contains(band.Frequency));
                    trial.Bands.AddRange(ProjectCodec.Clone(change.Bands));
                }

                trial.Bands = trial.Bands.OrderBy(band => band.Frequency).ToList();
            }
        });
    }

    public static string DecodeUtf8(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            throw new DomainValidationException("csv.encoding", "不是有效UTF-8，请另存为CSV UTF-8");
        }
    }
}

public static class CsvParser
{
    public static List<CsvRecord> Read(string content, char delimiter)
    {
        var records = new List<CsvRecord>();
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var closedQuote = false;
        var line = 1;
        var startLine = 1;
        for (var index = 0; index < content.Length; index++)
        {
            var character = content[index];
            if (quoted)
            {
                if (character == '"' && index + 1 < content.Length && content[index + 1] == '"')
                {
                    cell.Append('"');
                    index++;
                }
                else if (character == '"')
                {
                    quoted = false;
                    closedQuote = true;
                }
                else
                {
                    cell.Append(character);
                    if (character == '\n')
                    {
                        line++;
                    }
                }

                continue;
            }

            if (character == delimiter)
            {
                cells.Add(cell.ToString());
                cell.Clear();
                closedQuote = false;
            }
            else if (character is '\r' or '\n')
            {
                if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n')
                {
                    index++;
                }

                cells.Add(cell.ToString());
                if (cells.Any(value => value.Length > 0))
                {
                    records.Add(new CsvRecord(startLine, cells));
                }

                cells = [];
                cell.Clear();
                closedQuote = false;
                line++;
                startLine = line;
            }
            else if (character == '"' && cell.Length == 0 && !closedQuote)
            {
                quoted = true;
            }
            else if (closedQuote || character == '"')
            {
                throw new DomainValidationException("CSV第" + line + "行", "引号位置错误或闭引号后仍有内容");
            }
            else
            {
                cell.Append(character);
            }
        }

        if (quoted)
        {
            throw new DomainValidationException("CSV第" + startLine + "行", "引号未闭合");
        }

        if (cell.Length > 0 || cells.Count > 0 || closedQuote)
        {
            cells.Add(cell.ToString());
            records.Add(new CsvRecord(startLine, cells));
        }

        return records;
    }
}
