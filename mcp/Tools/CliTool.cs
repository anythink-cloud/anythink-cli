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
        "For destructive commands add '--yes' to skip confirmation prompts. " +
        "Add '--json' where supported for machine-readable output.")]
    public async Task<string> RunCli(
        [Description(
            "CLI arguments after 'anythink', e.g. 'entities list', 'users me', " +
            "'data list blog_posts --json', 'migrate --from a --to b --dry-run', 'fetch /some/path'. " +
            "Do NOT include 'anythink' itself or '--profile' (profile is injected automatically).")]
        string command)
    {
        var args = SplitArgs(command);
        if (args.Count == 0)
            return "Error: command must not be empty.";

        AnythinkClient? client;
        try
        {
            client = ResolveClient();
        }
        catch (InvalidOperationException ex)
        {
            return $"Error: {ex.Message}";
        }

        var result = await CliRunner.RunAsync(args, client);
        if (result.ExitCode != 0)
            return $"CLI exited with code {result.ExitCode}: {result.Output}";

        return result.Output.Length > 0 ? result.Output : "(no output)";
    }

    private AnythinkClient? ResolveClient()
    {
        try
        {
            return _factory.GetClient();
        }
        catch (InvalidOperationException) when (string.IsNullOrEmpty(_factory.ProfileName))
        {
            return null;
        }
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
