using AnythinkCli.Client;
using AnythinkCli.Config;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AnythinkCli.Commands;

// ── oauth google status ────────────────────────────────────────────────────

public class OAuthGoogleStatusCommand : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext ctx)
    {
        var profile = ConfigService.GetActiveProfile();
        if (profile is null) { Renderer.Error("No active profile. Run [bold]anythink projects use <id>[/]."); return 1; }

        var client = new AnythinkClient(profile);
        try
        {
            var settings = await client.GetGoogleOAuthAsync();
            if (settings is null)
            {
                Renderer.Warn("Google OAuth is not configured for this project.");
                AnsiConsole.MarkupLine("Run [bold #F97316]anythink oauth google configure[/] to set it up.");
                return 0;
            }

            var status = settings.Enabled ? "[green]Enabled[/]" : "[yellow]Disabled[/]";
            var hasId  = !string.IsNullOrEmpty(settings.ClientId);
            var hasSec = !string.IsNullOrEmpty(settings.ClientSecret);

            AnsiConsole.MarkupLine($"Google OAuth  {status}");
            AnsiConsole.MarkupLine($"  Client ID      {(hasId  ? "[green]✓ configured[/]" : "[red]✗ not set[/]")}");
            AnsiConsole.MarkupLine($"  Client secret  {(hasSec ? "[green]✓ configured[/]" : "[red]✗ not set[/]")}");

            if (!settings.Enabled || !hasId || !hasSec)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Run [bold #F97316]anythink oauth google configure[/] to update.[/]");
            }
            return 0;
        }
        catch (AnythinkException ex) { Renderer.Error(ex.Message); return 1; }
    }
}

// ── oauth google configure ─────────────────────────────────────────────────

public class OAuthGoogleConfigureCommand : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext ctx)
    {
        var profile = ConfigService.GetActiveProfile();
        if (profile is null) { Renderer.Error("No active profile. Run [bold]anythink projects use <id>[/]."); return 1; }

        AnsiConsole.MarkupLine("[dim]Configure Google OAuth credentials for this project.[/]");
        AnsiConsole.MarkupLine("[dim]You can find these in the Google Cloud Console → APIs & Services → Credentials.[/]");
        AnsiConsole.WriteLine();

        // Fetch existing to show current state
        var client = new AnythinkClient(profile);
        GoogleOAuthSettings? existing = null;
        try { existing = await client.GetGoogleOAuthAsync(); } catch { /* not yet configured */ }

        var hasExistingId  = !string.IsNullOrEmpty(existing?.ClientId);
        var hasExistingSec = !string.IsNullOrEmpty(existing?.ClientSecret);

        var clientId = AnsiConsole.Ask<string>(
            hasExistingId
                ? "[bold]Client ID[/] [dim](leave blank to keep existing)[/]:"
                : "[bold]Client ID[/]:");

        if (hasExistingId && string.IsNullOrWhiteSpace(clientId))
            clientId = existing!.ClientId!;

        if (string.IsNullOrWhiteSpace(clientId))
        {
            Renderer.Error("Client ID is required.");
            return 1;
        }

        var secretPrompt = hasExistingSec
            ? "[bold]Client secret[/] [dim](leave blank to keep existing)[/]:"
            : "[bold]Client secret[/]:";

        var clientSecret = AnsiConsole.Prompt(
            new TextPrompt<string>(secretPrompt)
                .Secret()
                .AllowEmpty());

        if (hasExistingSec && string.IsNullOrWhiteSpace(clientSecret))
            clientSecret = existing!.ClientSecret!;

        if (string.IsNullOrWhiteSpace(clientSecret))
        {
            Renderer.Error("Client secret is required.");
            return 1;
        }

        bool enabled = existing?.Enabled ?? true;
        if (existing is not null)
        {
            enabled = AnsiConsole.Confirm("Enable Google sign-in?", existing.Enabled);
        }

        try
        {
            await AnsiConsole.Status().StartAsync("Saving…", async _ =>
            {
                await client.PutGoogleOAuthAsync(new UpdateGoogleOAuthRequest(enabled, clientId, clientSecret));
            });

            var statusLabel = enabled ? "[green]enabled[/]" : "[yellow]disabled[/]";
            Renderer.Success($"Google OAuth credentials saved and {statusLabel}.");

            if (enabled)
            {
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine("[dim]Users can now sign in with their Google account.[/]");
                AnsiConsole.MarkupLine($"[dim]Callback URL: [/][bold]{Markup.Escape(profile.InstanceApiUrl)}/org/{Markup.Escape(profile.OrgId)}/auth/v1/google/callback[/]");
            }
            return 0;
        }
        catch (AnythinkException ex) { Renderer.Error(ex.Message); return 1; }
    }
}
