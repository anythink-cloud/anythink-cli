using AnythinkCli.Config;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

// ── config show ──────────────────────────────────────────────────────────────

public class ConfigShowCommand : Command<EmptySettings>
{
    public override int Execute(CommandContext context, EmptySettings settings)
    {
        var config = ConfigService.Load();

        if (config.Platforms.Count > 0)
        {
            Renderer.Header("Platform sessions");
            var platTable = Renderer.BuildTable("Platform", "myanythink URL", "Billing URL", "Token", "Account", "Active");
            foreach (var (key, p) in config.Platforms)
            {
                var hasToken = string.IsNullOrEmpty(p.Token)
                    ? "—"
                    : (p.IsTokenExpired ? "[yellow]expired[/]" : "[green]valid[/]");
                var marker = key == config.ActivePlatform ? "[green]●[/]" : "";
                platTable.AddRow(
                    key == config.ActivePlatform ? $"[bold]{Markup.Escape(key)}[/]" : Markup.Escape(key),
                    Markup.Escape(p.MyAnythinkUrl),
                    Markup.Escape(p.BillingUrl),
                    hasToken,
                    Markup.Escape(p.AccountId?[..Math.Min(8, p.AccountId.Length)] ?? "—"),
                    marker);
            }
            AnsiConsole.Write(platTable);
            AnsiConsole.WriteLine();
        }

        if (!config.Profiles.Any())
        {
            Renderer.Warn("No project profiles configured. Run [bold]anythink projects use[/] to connect to one.");
            AnsiConsole.MarkupLine($"\n[dim]Config file: {Markup.Escape(ConfigService.ConfigFilePath)}[/]");
            return 0;
        }

        Renderer.Header("Project profiles");
        var table = Renderer.BuildTable("Profile", "Org ID", "Auth", "Platform", "Instance API URL", "Active");

        foreach (var (key, p) in config.Profiles)
        {
            var auth      = !string.IsNullOrEmpty(p.ApiKey) ? "api-key" : "token";
            var isDefault = key == config.DefaultProfile ? "[green]●[/]" : "";
            table.AddRow(
                key == config.DefaultProfile ? $"[bold]{Markup.Escape(key)}[/]" : Markup.Escape(key),
                Markup.Escape(p.OrgId ?? "—"),
                auth,
                string.IsNullOrEmpty(p.PlatformKey) ? "[dim]untagged[/]" : Markup.Escape(p.PlatformKey),
                Markup.Escape(p.InstanceApiUrl ?? "—"),
                isDefault);
        }

        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"\n[dim]Config file: {Markup.Escape(ConfigService.ConfigFilePath)}[/]");
        return 0;
    }
}

// ── config use ───────────────────────────────────────────────────────────────

public class ConfigUseSettings : CommandSettings
{
    [CommandArgument(0, "<PROFILE>")]
    [Description("Profile name to activate")]
    public string Profile { get; set; } = "";
}

public class ConfigUseCommand : Command<ConfigUseSettings>
{
    public override int Execute(CommandContext context, ConfigUseSettings settings)
    {
        try
        {
            ConfigService.SetDefault(settings.Profile);
            Renderer.Success($"Active profile set to [#F97316]{Markup.Escape(settings.Profile)}[/]");
            return 0;
        }
        catch (InvalidOperationException ex)
        {
            Renderer.Error(ex.Message);
            return 1;
        }
    }
}

// ── config remove ─────────────────────────────────────────────────────────────

public class ConfigRemoveSettings : CommandSettings
{
    [CommandArgument(0, "<PROFILE>")]
    [Description("Profile name to remove")]
    public string Profile { get; set; } = "";
}

public class ConfigRemoveCommand : Command<ConfigRemoveSettings>
{
    public override int Execute(CommandContext context, ConfigRemoveSettings settings)
    {
        var config = ConfigService.Load();
        if (!config.Profiles.ContainsKey(settings.Profile))
        {
            Renderer.Error($"Profile '{settings.Profile}' not found.");
            return 1;
        }

        ConfigService.RemoveProfile(settings.Profile);
        Renderer.Success($"Profile [#F97316]{Markup.Escape(settings.Profile)}[/] removed.");
        return 0;
    }
}

public class ConfigResetCommand : Command
{
    public override int Execute(CommandContext context)
    {
        var confirm = AnsiConsole.Confirm(
            $"[yellow]Are you sure you want to reset your CLI config?[/]",
            defaultValue: false);
        if (!confirm) { Renderer.Info("Cancelled."); return 0; }
        
        ConfigService.Reset();
        Renderer.Success("Configuration reset. Please run [bold #F97316]anythink login[/] to re-authenticate.");
        return 0;
    }
}
