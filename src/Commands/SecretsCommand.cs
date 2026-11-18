using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

// ── secrets list ──────────────────────────────────────────────────────────────

[CommandGuide("Shows each secret's key, the users it is assigned to, and when it was created and updated; values are never returned. Use it to check a key exists before referencing it in a workflow as {{ $anythink.secrets.KEY }}, or before `secrets_update` or `secrets_delete`.")]
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

    [CommandOption("--value <VALUE>")]
    [Description("Secret value (use for non-interactive / scripted usage)")]
    public string? Value { get; set; }
}

[CommandGuide("Stores the value encrypted in the project under the key, for workflows to reference as {{ $anythink.secrets.KEY }}; it can never be read back. Pass it in value, as the hidden prompt can't be answered by a tool. To change an existing secret use `secrets_update`.")]
public class SecretsCreateCommand : BaseCommand<SecretCreateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SecretCreateSettings settings)
    {
        var value = settings.Value ?? AnsiConsole.Prompt(
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
                .StartAsync(Renderer.Status($"Creating secret '{settings.Key}'..."), async _ =>
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

    [CommandOption("--value <VALUE>")]
    [Description("Secret value (use for non-interactive / scripted usage)")]
    public string? Value { get; set; }
}

[CommandGuide("Overwrites the stored value of an existing key, for example to rotate a credential; the old value is lost and workflows referencing the key use the new one from their next run. Pass the new value in value. Use `secrets_create` for a key that doesn't exist yet.")]
public class SecretsUpdateCommand : BaseCommand<SecretKeySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, SecretKeySettings settings)
    {
        AnsiConsole.MarkupLine($"[dim]Rotating value for[/] [bold #F97316]{Markup.Escape(settings.Key)}[/][dim]. The existing value will be overwritten.[/]");

        var value = settings.Value ?? AnsiConsole.Prompt(
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
                .StartAsync(Renderer.Status($"Updating secret '{settings.Key}'..."), async _ =>
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

[CommandGuide("Removes the key and its encrypted value for good; workflows that reference it as {{ $anythink.secrets.KEY }} fail until it is created again. To change a value, use `secrets_update` instead.")]
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
                .StartAsync(Renderer.Status($"Deleting secret '{settings.Key}'..."), async _ =>
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
