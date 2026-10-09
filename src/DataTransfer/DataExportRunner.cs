using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnythinkCli.Client;

namespace AnythinkCli.DataTransfer;

internal sealed record DataExportOptions(
    string Entity, string Path, DataFormat Format, string? Filter = null, IReadOnlyList<string>? Fields = null,
    int PageSize = 200, bool Force = false, bool FormulaGuard = true);

internal static class DataExportRunner
{
    public const int MaxPageSize = 1000;
    public const string Stdout = "-";

    public static async Task<int> RunAsync(AnythinkClient client, DataExportOptions o, Stream? stdout = null, CancellationToken ct = default)
    {
        if (o.PageSize is < 1 or > MaxPageSize) throw new CliException($"--page-size must be between 1 and {MaxPageSize}.");
        var toStdout = o.Path == Stdout;
        var target = toStdout ? null : System.IO.Path.GetFullPath(o.Path);
        if (target is not null && File.Exists(target) && !o.Force)
            throw new CliException($"{Spectre.Console.Markup.Escape(o.Path)} already exists. Use [bold #F97316]--force[/] to overwrite it.");

        // One-to-many values are child-record references that can't be re-imported, so they're opt-in via --fields.
        var schema = o.Fields is null ? await client.GetFieldsAsync(o.Entity) : null;
        var excluded = schema?.Where(f => f.DatabaseType.Equals("one-to-many", StringComparison.OrdinalIgnoreCase))
            .Select(f => f.Name).ToHashSet(StringComparer.Ordinal) ?? [];
        var columns = schema?.Select(f => f.Name).Where(n => !excluded.Contains(n)).ToList();

        var temp = target is null ? null
            : System.IO.Path.Combine(System.IO.Path.GetDirectoryName(target)!, $".{System.IO.Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        var rows = 0;
        try
        {
            await using (var output = temp is null ? stdout ?? Console.OpenStandardOutput() : new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
            {
                await using var writer = new RowWriter(output, o, columns, excluded);
                for (var page = 1; ; page++)
                {
                    ct.ThrowIfCancellationRequested();
                    var result = await client.ListItemsAsync(o.Entity, page, o.PageSize, o.Filter);
                    foreach (var item in result.Items) { writer.Write(item); rows++; }
                    writer.Flush();
                    if (!result.HasNextPage || result.Items.Count == 0) break;
                }
                writer.Finish();
            }
            if (temp is not null) File.Move(temp, target!, overwrite: o.Force);
            return rows;
        }
        finally
        {
            if (temp is not null && File.Exists(temp)) File.Delete(temp);
        }
    }

    private sealed class RowWriter(Stream output, DataExportOptions o, List<string>? columns, HashSet<string> excluded) : IAsyncDisposable
    {
        private readonly StreamWriter? _text = o.Format == DataFormat.Json ? null : new StreamWriter(output, new UTF8Encoding(false), 64 * 1024, leaveOpen: true);
        private readonly Utf8JsonWriter? _json = o.Format == DataFormat.Json
            ? new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }) : null;
        private List<string>? _columns = o.Fields?.ToList() ?? (columns is { Count: > 0 } ? columns : null);
        private bool _started;

        public void Write(JsonObject item)
        {
            if (!_started)
            {
                _started = true;
                _json?.WriteStartArray();
                if (o.Format == DataFormat.Csv)
                {
                    _columns ??= item.Select(kv => kv.Key).Where(k => !excluded.Contains(k)).ToList();
                    Csv.WriteRow(_text!, _columns);
                }
            }

            switch (o.Format)
            {
                case DataFormat.Csv:
                    Csv.WriteRow(_text!, _columns!.Select(c => Csv.ToCell(item[c], o.FormulaGuard)));
                    break;
                case DataFormat.Jsonl:
                    _text!.WriteLine(Project(item).ToJsonString());
                    break;
                default:
                    Project(item).WriteTo(_json!);
                    break;
            }
        }

        private JsonObject Project(JsonObject item)
        {
            if (o.Fields is null)
            {
                foreach (var name in excluded) item.Remove(name);
                return item;
            }
            var picked = new JsonObject();
            foreach (var f in o.Fields) picked[f] = item[f]?.DeepClone();
            return picked;
        }

        public void Flush()
        {
            _json?.Flush();
            _text?.Flush();
            output.Flush();
        }

        public void Finish()
        {
            if (!_started && o.Format == DataFormat.Csv && _columns is not null) Csv.WriteRow(_text!, _columns);
            if (!_started) _json?.WriteStartArray();
            _json?.WriteEndArray();
            Flush();
        }

        public async ValueTask DisposeAsync()
        {
            if (_json is not null) await _json.DisposeAsync();
            if (_text is not null) await _text.DisposeAsync();
        }
    }
}
