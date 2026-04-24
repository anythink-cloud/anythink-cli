using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace AnythinkMcp.Tools;

/// <summary>
/// Generic MCP tool that shells out to the Anythink CLI.
/// Covers every command the CLI supports — useful as a catch-all for commands
/// that don't have dedicated MCP tool wrappers (accounts, projects, users,
/// files, pay, oauth, migrate, fetch, etc.).
///
/// Requires the <c>anythink</c> CLI to be installed and available on PATH
/// (e.g. via <c>dotnet tool install -g anythink-cli</c>).
/// </summary>
[McpServerToolType]
public class CliTool
{
    private readonly McpClientFactory _factory;
    public CliTool(McpClientFactory factory) => _factory = factory;

    // Allow only safe characters: alphanumeric, hyphens, underscores, dots, colons,
    // slashes, spaces, equals, commas, braces, brackets, quotes, and @.
    // Rejects shell metacharacters like ; | & $ ` \ ! ~ etc.
    private static readonly Regex SafeArgs = new(
        @"^[\w\s\-\./:=,@""'\{\}\[\]]+$", RegexOptions.Compiled);

    [McpServerTool(Name = "cli"),
     Description(
        "Run any Anythink CLI command and return its output. " +
        "Use this for commands not covered by dedicated tools (entities, fields, data, workflows, " +
        "roles, menus, secrets, users, files, pay, oauth, migrate, fetch, api, docs, etc.). " +
        "Pass the command exactly as you would after 'anythink', e.g. 'entities list' or 'data list posts'. " +
        "Menu commands: 'menus list' shows dashboard menus with tree structure; " +
        "'menus add-item <menu_id> <entity> --icon <Icon> --parent <parent_id>' adds an entity to a dashboard menu. " +
        "For destructive commands add '--yes' to skip confirmation prompts. " +
        "Add '--json' where supported for machine-readable output.")]
    public async Task<string> RunCli(
        [Description(
            "CLI arguments after 'anythink', e.g. 'entities list', 'users me', " +
            "'data list blog_posts --json', 'migrate --from a --to b --dry-run', 'fetch /some/path'. " +
            "Do NOT include 'anythink' itself or '--profile' (profile is injected automatically).")]
        string command)
    {
        // ── Input validation ────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(command))
            return "Error: command must not be empty.";

        if (!SafeArgs.IsMatch(command))
            return "Error: command contains disallowed characters.";

        // ── Build argument list (no shell involved — args passed directly) ──────
        var psi = new ProcessStartInfo
        {
            FileName = "anythink",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Suppress Spectre.Console ANSI sequences for cleaner output.
            Environment = { ["NO_COLOR"] = "1", ["TERM"] = "dumb" }
        };

        // Inject --profile if the MCP server was started with one.
        var profile = _factory.ProfileName;
        if (!string.IsNullOrEmpty(profile))
        {
            psi.ArgumentList.Add("--profile");
            psi.ArgumentList.Add(profile);
        }

        // Split the user command into individual arguments.
        foreach (var arg in SplitArgs(command))
            psi.ArgumentList.Add(arg);

        try
        {
            using var process = Process.Start(psi)!;
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var msg = !string.IsNullOrWhiteSpace(stderr) ? stderr.Trim() : stdout.Trim();
                return $"CLI exited with code {process.ExitCode}: {msg}";
            }

            return string.IsNullOrWhiteSpace(stdout) ? "(no output)" : stdout.Trim();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "Error: 'anythink' CLI not found on PATH. Install it with: dotnet tool install -g anythink-cli";
        }
    }

    /// <summary>
    /// Splits a command string into arguments, respecting quoted strings.
    /// E.g. <c>data create posts --data '{"title":"Hello"}'</c> →
    /// <c>["data", "create", "posts", "--data", "{\"title\":\"Hello\"}"]</c>
    /// </summary>
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
