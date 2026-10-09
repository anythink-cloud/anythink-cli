using AnythinkCli;
using AnythinkCli.Config;
using Spectre.Console;
using Spectre.Console.Cli;

// Load .env from the current working directory if present.
var dotEnvPath = Path.Combine(Directory.GetCurrentDirectory(), ".env");
if (File.Exists(dotEnvPath))
{
    foreach (var line in File.ReadAllLines(dotEnvPath))
    {
        var trimmed = line.Trim();
        if (trimmed.StartsWith('#') || !trimmed.Contains('=')) continue;
        var idx = trimmed.IndexOf('=');
        var key = trimmed[..idx].Trim();
        var val = trimmed[(idx + 1)..].Trim().Trim('"').Trim('\'');
        if (!string.IsNullOrEmpty(key) && Environment.GetEnvironmentVariable(key) is null)
            Environment.SetEnvironmentVariable(key, val);
    }
}

// ── Pre-parse --profile before Spectre.Console sees args ─────────────────────
// Strips "--profile <name>" (or "-p <name>") from the args array so Spectre
// doesn't reject it as an unknown option, and stores the value in ProfileContext
// for BaseCommand.GetClient() to use.
{
    var argList = args.ToList();
    for (var i = 0; i < argList.Count - 1; i++)
    {
        if (argList[i] is "--profile" or "-p")
        {
            ProfileContext.Current = argList[i + 1];
            argList.RemoveRange(i, 2);
            break;
        }
    }
    args = [.. argList];
}

var app = new CommandApp();

app.Configure(CliApp.Configure);

try
{
    return await app.RunAsync(args);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Fatal error:[/] {Markup.Escape(ex.Message)}");
    return 1;
}
