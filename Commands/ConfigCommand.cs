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

        if (!config.Profiles.Any())
        {
            Renderer.Warn("No profiles configured. Run [bold]anythink login[/].");
            return 0;
        }

        Renderer.Header("Anythink CLI Profiles");

        var table = Renderer.BuildTable("Profile", "Org ID", "Auth", "Base URL", "Active");

        foreach (var (key, p) in config.Profiles)
        {
            var auth = !string.IsNullOrEmpty(p.ApiKey) ? "api-key" : "token";
            var isDefault = key == config.DefaultProfile ? "[green]●[/]" : "";
            table.AddRow(
                key == config.DefaultProfile ? $"[bold]{Markup.Escape(key)}[/]" : Markup.Escape(key),
                Markup.Escape(p.OrgId),
                auth,
                Markup.Escape(p.BaseUrl),
                isDefault
            );
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
