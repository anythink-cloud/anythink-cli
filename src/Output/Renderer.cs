using Spectre.Console;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AnythinkCli.Output;

public static class Renderer
{
    public static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };

    public static void Success(string msg) => AnsiConsole.MarkupLine($"[green]✓[/] {msg}");
    public static void Info(string msg)    => AnsiConsole.MarkupLine($"[blue]i[/] {msg}");
    public static void Warn(string msg)    => AnsiConsole.MarkupLine($"[yellow]![/] {msg}");
    public static void Error(string msg)   => AnsiConsole.MarkupLine($"[red]✗[/] {msg}");

    public static void Header(string title)
    {
        AnsiConsole.Write(new Rule($"[bold #F97316]{Markup.Escape(title)}[/]").LeftJustified());
    }

    public static void KeyValue(string key, string? value, string color = "#F97316")
    {
        if (value is null)
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(key)}:[/] [dim]—[/]");
        else
            AnsiConsole.MarkupLine($"  [dim]{Markup.Escape(key)}:[/] [{color}]{Markup.Escape(value)}[/]");
    }

    public static void PrintJson(string json)
    {
        try
        {
            AnsiConsole.Write(new Spectre.Console.Json.JsonText(json));
            AnsiConsole.WriteLine();
        }
        catch
        {
            AnsiConsole.WriteLine(json);
        }
    }

    public static void PrintJsonObject(JsonNode? node)
    {
        if (node == null) { AnsiConsole.MarkupLine("[dim]null[/]"); return; }
        PrintJson(node.ToJsonString(PrettyJson));
    }

    public static Table BuildTable(params string[] columns)
    {
        var table = new Table().BorderColor(Color.Grey23).Border(TableBorder.Rounded);
        foreach (var col in columns)
            table.AddColumn(new TableColumn($"[bold #F97316]{Markup.Escape(col)}[/]"));
        return table;
    }

    public static void AddRow(Table table, params string?[] cells)
        => table.AddRow(cells.Select(c => Markup.Escape(c ?? "—")).ToArray());

    public static SelectionPrompt<T> Prompt<T>() where T : notnull => new() { HighlightStyle = new Style(foreground: new Color(196, 69, 54)) };

    /// <summary>Decodes the JWT payload and returns the best available display name.</summary>
    public static string? NameFromJwt(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;
            var payload = parts[1];
            payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var claim in new[] { "name", "first_name", "given_name", "preferred_username" })
                if (root.TryGetProperty(claim, out var el) && el.GetString() is { } v && !string.IsNullOrEmpty(v))
                    return v;
            if (root.TryGetProperty("email", out var emailEl) && emailEl.GetString() is { } e)
                return e.Split('@')[0];
        }
        catch
        {
            // ignored
        }

        return null;
    }

    public static void PrintWelcomeBanner(string? name = null)
    {
        string[] wordmark =
        [
            "   ░███                             ░██    ░██        ░██           ░██",
            "  ░██░██                            ░██    ░██                      ░██",
            " ░██  ░██  ░████████  ░██    ░██ ░████████ ░████████  ░██░████████  ░██    ░██",
            "░█████████ ░██    ░██ ░██    ░██    ░██    ░██    ░██ ░██░██    ░██ ░██   ░██",
            "░██    ░██ ░██    ░██ ░██    ░██    ░██    ░██    ░██ ░██░██    ░██ ░███████",
            "░██    ░██ ░██    ░██ ░██   ░███    ░██    ░██    ░██ ░██░██    ░██ ░██   ░██",
            "░██    ░██ ░██    ░██  ░█████░██     ░████ ░██    ░██ ░██░██    ░██ ░██    ░██",
            "                             ░██",
            "                       ░███████"
        ];
        foreach (var line in wordmark)
            AnsiConsole.MarkupLine($"[#F97316]{Markup.Escape(line)}[/]");
        AnsiConsole.WriteLine();
        if (!string.IsNullOrEmpty(name))
            AnsiConsole.MarkupLine($"[dim]Welcome back,[/] [bold white]{Markup.Escape(name)}[/]");
        AnsiConsole.MarkupLine("[dim]Whatever you're building, Anythink is the backend at your service.[/]");
        AnsiConsole.WriteLine();
    }
}
