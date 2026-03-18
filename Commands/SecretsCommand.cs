using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

// ── secrets list ──────────────────────────────────────────────────────────────

public class SecretsListCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();
            List<SecretResponse> secrets = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching secrets...", async _ =>
                {
                    secrets = await client.GetSecretsAsync();
                });

            if (secrets.Count == 0)
            {
                Renderer.Info("No secrets found.");
                AnsiConsole.MarkupLine("Run [bold #F97316]anythink secrets create <key>[/] to add one.");
                return 0;
            }

            Renderer.Header($"Secrets ({secrets.Count})");
            AnsiConsole.MarkupLine("[dim]Values are never displayed — keys and metadata only.[/]");
            AnsiConsole.WriteLine();

            var table = Renderer.BuildTable("Key", "Assigned Users", "Created", "Updated");
            foreach (var s in secrets.OrderBy(x => x.Key))
            {
                var users = s.Users.Count > 0
                    ? string.Join(", ", s.Users.Select(u => u.Email))
                    : "—";
                Renderer.AddRow(table,
                    s.Key,
                    users,
                    s.CreatedAt.ToString("yyyy-MM-dd"),
                    s.UpdatedAt.ToString("yyyy-MM-dd")
                );
            }

            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── secrets create ────────────────────────────────────────────────────────────

public class SecretCreateSettings : CommandSettings
{
    [CommandArgument(0, "<KEY>")]
    [Description("Secret key name (e.g. CLAUDE_API_KEY, STRIPE_SECRET_KEY)")]
    public string Key { get; set; } = "";
}

public class SecretsCreateCommand : BaseCommand<SecretCreateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SecretCreateSettings settings)
    {
        var value = AnsiConsole.Prompt(
            new TextPrompt<string>($"Value for [#F97316]{Markup.Escape(settings.Key)}[/]:")
                .Secret()
                .PromptStyle("grey"));

        if (string.IsNullOrEmpty(value))
        {
            Renderer.Error("Value cannot be empty.");
            return 1;
        }

        try
        {
            var client = GetClient();
            SecretResponse? created = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Creating secret '{settings.Key}'...", async _ =>
                {
                    created = await client.CreateSecretAsync(new CreateSecretRequest(settings.Key, value));
                });

            Renderer.Success($"Secret [#F97316]{Markup.Escape(created!.Key)}[/] created.");
            AnsiConsole.MarkupLine($"[dim]Reference in workflows as:[/] [bold]{{{{ $anythink.secrets.{Markup.Escape(settings.Key)} }}}}[/]");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── secrets update ────────────────────────────────────────────────────────────

public class SecretKeySettings : CommandSettings
{
    [CommandArgument(0, "<KEY>")]
    [Description("Secret key name")]
    public string Key { get; set; } = "";
}

public class SecretsUpdateCommand : BaseCommand<SecretKeySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SecretKeySettings settings)
    {
        AnsiConsole.MarkupLine($"[dim]Rotating value for[/] [bold #F97316]{Markup.Escape(settings.Key)}[/][dim]. The existing value will be overwritten.[/]");

        var value = AnsiConsole.Prompt(
            new TextPrompt<string>("New value:")
                .Secret()
                .PromptStyle("grey"));

        if (string.IsNullOrEmpty(value))
        {
            Renderer.Error("Value cannot be empty.");
            return 1;
        }

        try
        {
            var client = GetClient();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Updating secret '{settings.Key}'...", async _ =>
                {
                    await client.UpdateSecretAsync(settings.Key, new UpdateSecretRequest(value));
                });

            Renderer.Success($"Secret [#F97316]{Markup.Escape(settings.Key)}[/] rotated.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── secrets delete ────────────────────────────────────────────────────────────

public class SecretDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<KEY>")]
    [Description("Secret key name to delete")]
    public string Key { get; set; } = "";

    [CommandOption("--yes")]
    [Description("Skip confirmation prompt")]
    public bool Yes { get; set; }
}

public class SecretsDeleteCommand : BaseCommand<SecretDeleteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SecretDeleteSettings settings)
    {
        if (!settings.Yes)
        {
            var confirm = AnsiConsole.Confirm(
                $"[yellow]Delete secret[/] [bold red]{Markup.Escape(settings.Key)}[/][yellow]? Any workflows referencing it will break.[/]",
                defaultValue: false);
            if (!confirm) { Renderer.Info("Cancelled."); return 0; }
        }

        try
        {
            var client = GetClient();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Deleting secret '{settings.Key}'...", async _ =>
                {
                    await client.DeleteSecretAsync(settings.Key);
                });

            Renderer.Success($"Secret [#F97316]{Markup.Escape(settings.Key)}[/] deleted.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
