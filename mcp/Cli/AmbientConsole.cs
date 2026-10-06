using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace AnythinkMcp.Cli;

internal sealed class AmbientConsole : IAnsiConsole
{
    private static readonly AsyncLocal<(TextWriter Writer, IAnsiConsole Console)?> Ambient = new();
    private static readonly IAnsiConsole Discard = Create(TextWriter.Null);

    public static readonly AmbientConsole Instance = new();

    public static TextWriter? Writer => Ambient.Value?.Writer;

    private static IAnsiConsole Current => Ambient.Value?.Console ?? Discard;

    public static void Install()
    {
        AnsiConsole.Console = Instance;
        Console.SetOut(new AmbientWriter(Console.Out));
        Console.SetError(new AmbientWriter(Console.Error));
    }

    public static void Use(TextWriter writer) => Ambient.Value = (writer, Create(writer));

    private static IAnsiConsole Create(TextWriter writer)
    {
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Interactive = InteractionSupport.No,
            Out = new AnsiConsoleOutput(writer)
        });
        console.Profile.Width = 4096;
        return console;
    }

    public Profile Profile => Current.Profile;

    public IAnsiConsoleCursor Cursor => Current.Cursor;

    public IAnsiConsoleInput Input => Current.Input;

    public IExclusivityMode ExclusivityMode => Current.ExclusivityMode;

    // Spinners hook the pipeline to print their status text; tool output shouldn't carry it
    public RenderPipeline Pipeline { get; } = new();

    public void Clear(bool home) => Current.Clear(home);

    public void Write(IRenderable renderable)
    {
        switch (renderable)
        {
            case Table table:
                Current.Profile.Out.Writer.WriteLine(TableJson(table));
                break;
            case Rule rule:
                if (!string.IsNullOrWhiteSpace(rule.Title))
                    Current.Profile.Out.Writer.WriteLine(Markup.Remove(rule.Title).Trim());
                break;
            default:
                Current.Write(renderable);
                break;
        }
    }

    internal static string TableJson(Table table)
    {
        var headers = new List<string>();
        foreach (var (column, i) in table.Columns.Select((column, i) => (column, i)))
        {
            var header = PlainText(column.Header) is { Length: > 0 } text ? text : $"Column {i + 1}";
            var key = header;
            for (var n = 2; headers.Contains(key); n++)
                key = $"{header} {n}";
            headers.Add(key);
        }

        var rows = table.Rows.Select(row =>
        {
            var item = new JsonObject();
            foreach (var (cell, i) in row.Select((cell, i) => (cell, i)).Where(c => c.i < headers.Count))
                item[headers[i]] = PlainText(cell);
            return item.ToJsonString(Compact);
        });

        return "[\n" + string.Join(",\n", rows) + "\n]";
    }

    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string PlainText(IRenderable renderable)
    {
        var options = RenderOptions.Create(Discard, Discard.Profile.Capabilities);
        return string.Concat(renderable.Render(options, 100_000).Select(s => s.IsLineBreak ? "\n" : s.Text)).Trim();
    }

    private sealed class AmbientWriter(TextWriter fallback) : TextWriter
    {
        private TextWriter Target => Writer ?? fallback;

        public override Encoding Encoding => fallback.Encoding;

        public override void Write(char value) => Target.Write(value);

        public override void Write(char[] buffer, int index, int count) => Target.Write(buffer, index, count);

        public override void Write(string? value) => Target.Write(value);

        public override void WriteLine(string? value) => Target.WriteLine(value);

        public override void Flush() => Target.Flush();
    }
}
