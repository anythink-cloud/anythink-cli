using System.ComponentModel;
using AnythinkCli.Client;
using AnythinkMcp.Cli;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

[McpServerToolType]
public class CliTool
{
    private readonly McpClientFactory _factory;
    public CliTool(McpClientFactory factory) => _factory = factory;

    [McpServerTool(Name = "cli"),
     Description(
        "Run any Anythink CLI command and return its output. " +
        "Prefer the dedicated tools; use this for anything they don't cover. " +
        "Pass the command exactly as you would after 'anythink', e.g. 'entities list' or 'data list posts'. " +
        "Destructive pay commands (delete, expire, relink, pause and similar) must be run by a person in a terminal; never add '--yes' on their behalf. " +
        "Add '--json' where supported for machine-readable output.")]
    public async Task<string> RunCli(
        [Description(
            "CLI arguments after 'anythink', e.g. 'entities list', 'users me', " +
            "'data list blog_posts --json', 'migrate --from a --to b --dry-run', 'fetch /some/path'. " +
            "Do NOT include 'anythink' itself or '--profile' (profile is injected automatically).")]
        string command,
        CancellationToken cancellationToken = default)
    {
        var args = SplitArgs(command);
        if (args.Count == 0)
            return "Error: command must not be empty.";

        if (RefusalFor(args) is { } refusal)
            return refusal;

        AnythinkClient? client;
        try
        {
            client = _factory.GetClientOrNull();
        }
        catch (InvalidOperationException ex)
        {
            return $"Error: {ex.Message}";
        }

        var result = await CliRunner.RunAsync(args, client, CliToolScope.Local, cancellationToken);
        if (result.ExitCode != 0)
            return $"CLI exited with code {result.ExitCode}: {result.Output}";

        return result.Output.Length > 0 ? result.Output : "(no output)";
    }

    private const string PayRefusal =
        "Refused: this changes billing state and cannot be run through the cli tool. " +
        "Use the dedicated pay tools (which ask for confirmation) or ask a person to run it in a terminal.";

    internal static string? RefusalFor(string command) => RefusalFor(SplitArgs(command));

    private static string? RefusalFor(List<string> args)
    {
        var words = args.Where(a => !a.StartsWith('-')).Select(a => a.ToLowerInvariant()).ToList();
        if (words is not ["pay", ..]) return null;

        var refused = (words.ElementAtOrDefault(1), words.ElementAtOrDefault(2)) switch
        {
            ("subscriptions", "delete" or "force-expire" or "relink" or "resync" or "cancel") => true,
            ("plans", "delete") => true,
            ("offers", "delete" or "pause") => true,
            ("offers", "update") => StatusArg(args) is "paused" or "expired",
            ("apple", "credentials" or "verify") => true,
            _ => false
        };
        return refused ? PayRefusal : null;
    }

    private static string? StatusArg(List<string> args)
    {
        var i = args.FindIndex(a => a == "--status");
        if (i >= 0 && i + 1 < args.Count) return args[i + 1].ToLowerInvariant();
        return args.FirstOrDefault(a => a.StartsWith("--status=", StringComparison.Ordinal))?[9..].ToLowerInvariant();
    }

    internal static List<string> SplitArgs(string input)
    {
        var args = new List<string>();
        var current = "";
        var inSingle = false;
        var inDouble = false;

        foreach (var c in input)
        {
            if (c == '\'' && !inDouble) { inSingle = !inSingle; continue; }
            if (c == '"' && !inSingle) { inDouble = !inDouble; continue; }
            if (c == ' ' && !inSingle && !inDouble)
            {
                if (current.Length > 0) { args.Add(current); current = ""; }
                continue;
            }
            current += c;
        }
        if (current.Length > 0) args.Add(current);

        return args;
    }
}
