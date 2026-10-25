using AnythinkCli.Client;
using Anythink.ProjectImporters;
using Spectre.Console;

namespace AnythinkCli.Commands;

internal static class ImportCommandSupport
{
    public static int ExitCodeFor(ImportResult result) => result.Errors.Count > 0 ? 1 : 0;

    public static bool IsInteractive() => !Console.IsInputRedirected && AnsiConsole.Profile.Capabilities.Interactive;

    public static void RequireConfirmationMode(bool dryRun, bool yes, bool interactive)
    {
        if (!dryRun && !yes && !interactive)
            throw new CliException("Refusing to modify the project without confirmation while not running interactively. Re-run with [bold #F97316]--yes[/] to apply, or [bold #F97316]--dry-run[/] to preview.");
    }
}
