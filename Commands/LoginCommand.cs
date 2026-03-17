using AnythinkCli.Config;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

public class LogoutSettings : CommandSettings
{
    [CommandOption("--profile <NAME>")]
    [Description("Profile to remove (default: active profile)")]
    public string? Profile { get; set; }
}

public class LogoutCommand : Command<LogoutSettings>
{
    public override int Execute(CommandContext context, LogoutSettings settings)
    {
        var config = ConfigService.Load();
        var key = settings.Profile ?? config.DefaultProfile;

        if (string.IsNullOrEmpty(key) || !config.Profiles.ContainsKey(key))
        {
            Renderer.Error($"Profile '{key}' not found.");
            return 1;
        }

        ConfigService.RemoveProfile(key);
        Renderer.Success($"Profile [#F97316]{Markup.Escape(key)}[/] removed.");
        return 0;
    }
}
