using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

// ── email templates list ──────────────────────────────────────────────────────

public class EmailTemplatesListCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();
            List<EmailTemplate> templates = [];
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching email templates...", async _ =>
                {
                    templates = await client.GetEmailTemplatesAsync();
                });

            if (templates.Count == 0) { Renderer.Info("No email templates found."); return 0; }
            Renderer.Header($"Email templates ({templates.Count})");
            var table = Renderer.BuildTable("Type", "Subject", "Locked", "Active");
            foreach (var t in templates.OrderBy(x => x.TemplateType))
            {
                Renderer.AddRow(table, t.TemplateType, t.Subject, t.Locked ? "yes" : "no", t.IsActive ? "yes" : "no");
            }
            AnsiConsole.Write(table);
            return 0;
        }
        catch (Exception ex) { Renderer.Error(ex.Message); return 1; }
    }
}

// ── email templates show ──────────────────────────────────────────────────────

public class EmailTemplateShowSettings : CommandSettings
{
    [CommandArgument(0, "<template-type>")]
    [Description("The template type (e.g. confirmation, invite, recovery)")]
    public string TemplateType { get; set; } = string.Empty;

    [CommandOption("--rendered")]
    [Description("Show the rendered HTML using sample data instead of the raw content")]
    public bool Rendered { get; set; }
}

public class EmailTemplateShowCommand : BaseCommand<EmailTemplateShowSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmailTemplateShowSettings settings)
    {
        try
        {
            var client = GetClient();
            if (settings.Rendered)
            {
                EmailTemplatePreview preview = null!;
                await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                    .StartAsync("Rendering preview...", async _ =>
                    {
                        preview = await client.PreviewEmailTemplateAsync(settings.TemplateType);
                    });
                AnsiConsole.MarkupLine($"[bold]Subject:[/] {Markup.Escape(preview.Subject)}");
                AnsiConsole.WriteLine();
                AnsiConsole.WriteLine(preview.HtmlContent);
                return 0;
            }

            EmailTemplate t = null!;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching template...", async _ =>
                {
                    t = await client.GetEmailTemplateAsync(settings.TemplateType);
                });
            AnsiConsole.MarkupLine($"[bold]Type:[/] {Markup.Escape(t.TemplateType)}");
            AnsiConsole.MarkupLine($"[bold]Subject:[/] {Markup.Escape(t.Subject)}");
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine(t.Content);
            return 0;
        }
        catch (Exception ex) { Renderer.Error(ex.Message); return 1; }
    }
}

// ── email templates update ────────────────────────────────────────────────────

public class EmailTemplateUpdateSettings : CommandSettings
{
    [CommandArgument(0, "<template-type>")]
    [Description("The template type to update")]
    public string TemplateType { get; set; } = string.Empty;

    [CommandOption("--subject <SUBJECT>")]
    public string? Subject { get; set; }

    [CommandOption("--content <PATH>")]
    [Description("Path to a file containing the new HTML/text content (use '-' for stdin)")]
    public string? ContentPath { get; set; }
}

public class EmailTemplateUpdateCommand : BaseCommand<EmailTemplateUpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmailTemplateUpdateSettings settings)
    {
        try
        {
            var client = GetClient();
            var current = await client.GetEmailTemplateAsync(settings.TemplateType);
            var subject = settings.Subject ?? current.Subject;
            var content = settings.ContentPath switch
            {
                null => current.Content,
                "-" => await Console.In.ReadToEndAsync(),
                _ => await File.ReadAllTextAsync(settings.ContentPath),
            };
            var updated = await client.UpdateEmailTemplateAsync(settings.TemplateType,
                new UpdateEmailTemplateRequest(subject, content));
            Renderer.Success($"Updated '{Markup.Escape(updated!.TemplateType)}'.");
            return 0;
        }
        catch (Exception ex) { Renderer.Error(ex.Message); return 1; }
    }
}

// ── email templates preview-raw ───────────────────────────────────────────────

public class EmailPreviewRawSettings : CommandSettings
{
    [CommandOption("--subject <SUBJECT>")]
    public string? Subject { get; set; }

    [CommandOption("--content <PATH>")]
    [Description("Path to file containing content (or '-' for stdin)")]
    public string? ContentPath { get; set; }

    [CommandOption("--wrapper <PATH>")]
    [Description("Optional path to a wrapper HTML override (or '-' for stdin)")]
    public string? WrapperPath { get; set; }
}

public class EmailPreviewRawCommand : BaseCommand<EmailPreviewRawSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmailPreviewRawSettings settings)
    {
        try
        {
            var client = GetClient();
            var subject = settings.Subject ?? "Preview";
            var content = await ReadOrEmpty(settings.ContentPath);
            var wrapper = await ReadOrNull(settings.WrapperPath);
            var preview = await client.PreviewRawEmailAsync(new PreviewRawEmailRequest(subject, content, wrapper));
            AnsiConsole.WriteLine(preview.HtmlContent);
            return 0;
        }
        catch (Exception ex) { Renderer.Error(ex.Message); return 1; }
    }

    private static async Task<string> ReadOrEmpty(string? path) =>
        path == null ? string.Empty : path == "-" ? await Console.In.ReadToEndAsync() : await File.ReadAllTextAsync(path);
    private static async Task<string?> ReadOrNull(string? path) =>
        path == null ? null : path == "-" ? await Console.In.ReadToEndAsync() : await File.ReadAllTextAsync(path);
}

// ── email shell show / update / reset ─────────────────────────────────────────

public class EmailShellShowCommand : BaseCommand<EmptySettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmptySettings settings)
    {
        try
        {
            var client = GetClient();
            EmailShell shell = null!;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching shell...", async _ =>
                {
                    shell = await client.GetEmailShellAsync();
                });
            AnsiConsole.MarkupLine(shell.IsCustomised ? "[violet]Customised wrapper[/]" : "[gray]Using platform default[/]");
            AnsiConsole.WriteLine();
            AnsiConsole.WriteLine(shell.Html);
            return 0;
        }
        catch (Exception ex) { Renderer.Error(ex.Message); return 1; }
    }
}

public class EmailShellUpdateSettings : CommandSettings
{
    [CommandOption("--html <PATH>")]
    [Description("Path to file with new shell HTML (or '-' for stdin). Omit to reset.")]
    public string? HtmlPath { get; set; }

    [CommandOption("--reset")]
    [Description("Reset the shell to the platform default")]
    public bool Reset { get; set; }
}

public class EmailShellUpdateCommand : BaseCommand<EmailShellUpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmailShellUpdateSettings settings)
    {
        try
        {
            var client = GetClient();
            string? html = settings.Reset
                ? null
                : settings.HtmlPath switch
                {
                    null => null,
                    "-" => await Console.In.ReadToEndAsync(),
                    var p => await File.ReadAllTextAsync(p),
                };
            var result = await client.UpdateEmailShellAsync(new UpdateEmailShellRequest(html));
            Renderer.Success(settings.Reset ? "Shell reset to default." : "Shell saved.");
            return 0;
        }
        catch (Exception ex) { Renderer.Error(ex.Message); return 1; }
    }
}
