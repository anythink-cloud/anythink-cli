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
        "Use this for commands not covered by dedicated tools (accounts, projects, users, files, " +
        "pay, oauth, migrate, config, fetch, api, docs, etc.). " +
        "Pass the command exactly as you would after 'anythink', e.g. 'accounts list' or 'projects list'. " +
        "For destructive commands add '--yes' to skip confirmation prompts. " +
        "Add '--json' where supported for machine-readable output.")]
    public async Task<string> RunCli(
        [Description(
            "CLI arguments after 'anythink', e.g. 'accounts list', 'users me', " +
            "'projects list', 'migrate --from a --to b --dry-run', 'fetch /some/path'. " +
            "Do NOT include 'anythink' itself or '--profile' (profile is injected automatically).")]
        string command)
    {
        // ── Input validation ────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(command))
            return "Error: command must not be empty.";

        if (!SafeArgs.IsMatch(command))
            return "Error: command contains disallowed characters.";

        // ── Resolve CLI project path ────────────────────────────────────────────
        var mpcDir = Path.GetDirectoryName(typeof(CliTool).Assembly.Location)!;
        // Walk up from bin/Debug/net8.0 → mcp/ → repo root → src/
        var repoRoot = Path.GetFullPath(Path.Combine(mpcDir, "..", "..", "..", ".."));
        var cliProject = Path.Combine(repoRoot, "src");

        if (!Directory.Exists(cliProject))
            return $"Error: CLI project not found at {cliProject}";

        // ── Build argument list (no shell involved — args passed directly) ──────
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            // Suppress Spectre.Console ANSI sequences for cleaner output.
            Environment = { ["NO_COLOR"] = "1", ["TERM"] = "dumb" }
        };

        // Use ArgumentList for safe argument passing (no shell interpolation).
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(cliProject);
        psi.ArgumentList.Add("--");

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
