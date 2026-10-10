using System.Text;
using System.Text.Json.Nodes;

namespace AnythinkCli.DataTransfer;

internal static class Csv
{
    private static readonly char[] FormulaStarts = ['=', '+', '-', '@', '\t', '\r'];

    public const int MaxRecordChars = 16 * 1024 * 1024;

    public static IEnumerable<List<string>> ReadRecords(TextReader reader, int maxRecordChars = MaxRecordChars)
    {
        var record = new List<string>();
        var cell = new StringBuilder();
        bool inQuotes = false, quoted = false;
        var size = 0;
        int c;
        while ((c = reader.Read()) >= 0)
        {
            var ch = (char)c;
            if (++size > maxRecordChars)
                throw new FormatException($"a single row is larger than {maxRecordChars / (1024 * 1024)} MB; check for an unbalanced quote");
            if (inQuotes)
            {
                if (ch != '"') cell.Append(ch);
                else if (reader.Peek() == '"') { reader.Read(); cell.Append('"'); }
                else inQuotes = false;
                continue;
            }
            if (ch == '"' && cell.Length == 0 && !quoted) { inQuotes = quoted = true; }
            else if (ch == ',') { record.Add(cell.ToString()); cell.Clear(); quoted = false; }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && reader.Peek() == '\n') reader.Read();
                record.Add(cell.ToString());
                cell.Clear();
                quoted = false;
                yield return record;
                record = new List<string>();
                size = 0;
            }
            else cell.Append(ch);
        }
        if (inQuotes) throw new FormatException("a quoted cell is never closed");
        if (cell.Length > 0 || quoted || record.Count > 0)
        {
            record.Add(cell.ToString());
            yield return record;
        }
    }

    public static void WriteRow(TextWriter w, IEnumerable<string?> cells)
    {
        var first = true;
        foreach (var cell in cells)
        {
            if (!first) w.Write(',');
            first = false;
            w.Write(Escape(cell));
        }
        w.Write("\r\n");
    }

    public static string Escape(string? cell)
    {
        if (string.IsNullOrEmpty(cell)) return "";
        if (cell.IndexOfAny([',', '"', '\r', '\n']) < 0) return cell;
        return "\"" + cell.Replace("\"", "\"\"") + "\"";
    }

    // Numbers are never guarded so negatives survive; only text can carry a spreadsheet formula.
    public static string? ToCell(JsonNode? value, bool formulaGuard)
    {
        if (value is null) return null;
        if (value is JsonValue v && v.TryGetValue<string>(out var text))
            return formulaGuard && text.Length > 0 && Array.IndexOf(FormulaStarts, text[0]) >= 0 ? "'" + text : text;
        return value.ToJsonString();
    }
}
