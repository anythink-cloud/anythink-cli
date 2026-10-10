using System.Threading.Channels;
using System.Text.Json.Nodes;
using AnythinkCli.Client;

namespace AnythinkCli.DataTransfer;

internal sealed record DataImportOptions(
    string Entity, string FilePath, DataFormat? Format = null, bool DryRun = false, int Concurrency = 4,
    string? ErrorsPath = null, bool Yes = false, bool IgnoreUnknown = false, bool Interactive = false);

internal sealed record DataImportResult(
    int Created, int Failed, int Skipped, int WouldCreate, bool Cancelled, IReadOnlyList<string> Problems,
    IReadOnlyList<string> Notes, bool Interrupted = false)
{
    public bool HasProblems => Failed + Skipped > 0 || Interrupted;
}

internal sealed class RateGate
{
    private long _until;

    public void Pause(TimeSpan wait)
    {
        var target = DateTime.UtcNow.Ticks + wait.Ticks;
        long seen;
        while (target > (seen = Interlocked.Read(ref _until))) Interlocked.CompareExchange(ref _until, target, seen);
    }

    public TimeSpan Remaining(DateTime now) => TimeSpan.FromTicks(Math.Max(0, Interlocked.Read(ref _until) - now.Ticks));
}

internal sealed class DataImportRunner(
    AnythinkClient client,
    Func<TimeSpan, CancellationToken, Task>? delay = null,
    Func<string, bool>? confirm = null)
{
    public const int MaxConcurrency = 16;
    internal const int MaxAttempts = 8;
    private const int MaxReportedProblems = 20;
    private static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(120);

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = delay ?? Task.Delay;
    private readonly RateGate _gate = new();

    public async Task<DataImportResult> RunAsync(DataImportOptions o, CancellationToken ct = default)
    {
        if (o.Concurrency is < 1 or > MaxConcurrency)
            throw new CliException($"--concurrency must be between 1 and {MaxConcurrency}.");
        if (!File.Exists(o.FilePath)) throw new CliException($"File not found: {Spectre.Console.Markup.Escape(o.FilePath)}");
        if (o.ErrorsPath is not null && Path.GetFullPath(o.ErrorsPath) == Path.GetFullPath(o.FilePath))
            throw new CliException("--errors must not be the same file as the one being imported.");

        var fields = await client.GetFieldsAsync(o.Entity);
        if (fields.Count == 0) throw new CliException($"Entity '{Spectre.Console.Markup.Escape(o.Entity)}' has no fields. Check the name with [bold #F97316]anythink entities list[/].");

        await using var stream = new FileStream(o.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        var format = RowReader.Detect(o.Format, o.FilePath, stream);

        if (!o.DryRun && !o.Yes)
        {
            if (!o.Interactive)
                throw new CliException("Refusing to import without confirmation in a non-interactive session. Run with [bold #F97316]--dry-run[/] first, then add [bold #F97316]--yes[/].");
            var estimate = EstimateRows(o.FilePath, format) is { } n ? $"about {n}" : "unknown";
            if (!(confirm ?? DefaultConfirm)($"Import into [bold #F97316]{Spectre.Console.Markup.Escape(o.Entity)}[/] from {Spectre.Console.Markup.Escape(o.FilePath)} ({estimate} rows)?"))
                return new DataImportResult(0, 0, 0, 0, true, [], []);
        }

        var coercer = new RowCoercer(fields, o.IgnoreUnknown);
        var reader = new RowReader(stream, format);
        var problems = new List<string>();
        ErrorSink? sink = null;
        int created = 0, failed = 0, skipped = 0, wouldCreate = 0;
        var interrupted = false;

        void Record(RawRow row, string error, bool wasSent)
        {
            if (wasSent) Interlocked.Increment(ref failed); else Interlocked.Increment(ref skipped);
            lock (problems)
            {
                if (problems.Count < MaxReportedProblems) problems.Add($"row {row.Number}: {error}");
                sink?.Write(row, error);
            }
        }

        var channel = Channel.CreateBounded<(RawRow Row, JsonObject Payload)>(o.Concurrency * 2);
        var workers = o.DryRun ? Array.Empty<Task>() : Enumerable.Range(0, o.Concurrency).Select(_ => Task.Run(async () =>
        {
            await foreach (var (row, payload) in channel.Reader.ReadAllAsync())
            {
                if (await SendAsync(o.Entity, payload, ct) is { } error) Record(row, error, wasSent: true);
                else Interlocked.Increment(ref created);
            }
        })).ToArray();

        try
        {
            await foreach (var row in reader.ReadAsync())
            {
                if (ct.IsCancellationRequested) { interrupted = true; break; }
                if (o.ErrorsPath is not null) sink ??= new ErrorSink(o.ErrorsPath, format, reader.Header);
                if (row.Error is not null) { Record(row, row.Error, wasSent: false); continue; }
                if (!coercer.TryCoerce(row.Values!, format == DataFormat.Csv, out var payload, out var error))
                {
                    Record(row, error!, wasSent: false);
                    continue;
                }
                if (o.DryRun) wouldCreate++;
                else await channel.Writer.WriteAsync((row, payload));
            }
        }
        finally
        {
            channel.Writer.TryComplete();
            await Task.WhenAll(workers);
            sink?.Dispose();
        }

        var notes = coercer.Notes().ToList();
        if (o.ErrorsPath is not null && sink is not { Wrote: true } && File.Exists(o.ErrorsPath))
        {
            File.Delete(o.ErrorsPath);
            notes.Add($"removed {o.ErrorsPath}, left over from an earlier run (this run had no problem rows)");
        }
        return new DataImportResult(created, failed, skipped, wouldCreate, false, problems, notes, interrupted);
    }

    private async Task<string?> SendAsync(string entity, JsonObject payload, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var remaining = _gate.Remaining(DateTime.UtcNow);
                if (remaining > TimeSpan.Zero) await _delay(remaining, ct);
                var item = await client.CreateItemAsync(entity, payload);
                return item?["id"] is null ? Uncertain("unexpected response, no id returned") : null;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return "interrupted before the retry; the record was not created";
            }
            // The client can't see the status of an empty-bodied response; 201 is the created status.
            catch (AnythinkException e) when (e.StatusCode == 201) { return null; }
            catch (AnythinkException e) when (e.StatusCode is >= 200 and < 300) { return Uncertain($"unexpected response (HTTP {e.StatusCode})"); }
            catch (Exception e)
            {
                var wait = attempt + 1 < MaxAttempts ? WaitFor(e, attempt) : null;
                if (wait is null) return IsUncertain(e) ? Uncertain(Describe(e)) : Describe(e);
                _gate.Pause(wait.Value);
            }
        }
    }

    private static string Uncertain(string detail) => $"UNCERTAIN: may have been created, check before re-importing ({detail})";

    internal static bool NeverSent(Exception e) =>
        e is HttpRequestException { HttpRequestError: HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.SecureConnectionError };

    internal static bool IsUncertain(Exception e) =>
        e is AnythinkException api ? api.StatusCode is 502 or 504 : !NeverSent(e);

    // Only throttling and requests that never left the machine are retried: anything else could repeat a create.
    internal static TimeSpan? WaitFor(Exception e, int attempt)
    {
        if (e is AnythinkException { StatusCode: 429 or 503 } api)
        {
            if (api.RetryAfter is { } asked) return asked > MaxWait ? MaxWait : asked;
        }
        else if (!NeverSent(e)) return null;

        var backoff = Math.Min(30_000, 500 * Math.Pow(2, attempt));
        return TimeSpan.FromMilliseconds(backoff * (1 + Random.Shared.NextDouble() * 0.25));
    }

    private static string Describe(Exception e)
    {
        var text = e is AnythinkException api ? $"HTTP {api.StatusCode}: {api.Message}" : e.Message;
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length > 300 ? text[..300] + "…" : text;
    }

    private static bool DefaultConfirm(string prompt) => Spectre.Console.AnsiConsole.Confirm(prompt, defaultValue: false);

    internal static int? EstimateRows(string path, DataFormat format)
    {
        if (format == DataFormat.Json) return null;
        var lines = 0;
        var last = (byte)'\n';
        var buffer = new byte[64 * 1024];
        using var s = File.OpenRead(path);
        int read;
        while ((read = s.Read(buffer)) > 0)
        {
            lines += buffer.AsSpan(0, read).Count((byte)'\n');
            last = buffer[read - 1];
        }
        if (last != '\n') lines++;
        return Math.Max(0, format == DataFormat.Csv ? lines - 1 : lines);
    }
}
