using System.Text;

namespace TrimContact.Core.IO;

public sealed record CsvRecord(int Line, IReadOnlyList<string> Cells);

public static class CsvTable
{
    public static IReadOnlyList<CsvRecord> Read(string text)
    {
        if (Encoding.UTF8.GetByteCount(text) > 128 * 1024)
            throw new InvalidDataException("CSV最多128KB");
        text = text.TrimStart('\uFEFF');
        var rows = new List<CsvRecord>();
        var cells = new List<string>();
        var field = new StringBuilder();
        bool quoted = false;
        bool closedQuote = false;
        bool fieldStarted = false;
        int line = 1;
        int recordLine = 1;
        void EndField()
        {
            cells.Add(field.ToString());
            field.Clear();
            closedQuote = false;
            fieldStarted = false;
        }
        void EndRecord()
        {
            EndField();
            rows.Add(new CsvRecord(recordLine, cells.ToArray()));
            cells.Clear();
            if (rows.Count > 101)
                throw new InvalidDataException("CSV最多100条数据记录，加一行表头");
            recordLine = line + 1;
        }
        for (int i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                        closedQuote = true;
                    }
                }
                else
                {
                    field.Append(c);
                    if (c == '\n')
                        line++;
                }
                continue;
            }
            if (c == ',')
            {
                EndField();
                continue;
            }
            if (c is '\r' or '\n')
            {
                EndRecord();
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                    i++;
                line++;
                recordLine = line;
                continue;
            }
            if (closedQuote)
                throw new InvalidDataException($"CSV第{line}行：闭引号后只能是分隔符或换行");
            if (c == '"')
            {
                if (fieldStarted)
                    throw new InvalidDataException($"CSV第{line}行：未引用字段中含引号");
                quoted = true;
                fieldStarted = true;
                continue;
            }
            field.Append(c);
            fieldStarted = true;
        }
        if (quoted)
            throw new InvalidDataException($"CSV第{recordLine}行：引号未闭合");
        if (field.Length > 0 || cells.Count > 0 || closedQuote)
            EndRecord();
        if (rows.Count < 2)
            throw new InvalidDataException("CSV至少包含表头和一条数据");
        return rows;
    }
}
