using AnythinkCli.Client;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

internal static class EmailInput
{
    public const int MaxCharacters = 1_000_000;

    public static async Task<string?> ReadAsync(string? path, string? inline, string pathOption, string inlineOption)
    {
        if (path is not null && inline is not null)
            throw new InvalidOperationException($"Use either {pathOption} or {inlineOption}, not both.");

        if (path is not null && ClientContext.Remote)
            throw new InvalidOperationException($"{pathOption} reads a file on this machine and can't be used remotely. Pass {inlineOption} instead.");

        var text = path switch
        {
            null => inline,
            "-" => await Console.In.ReadToEndAsync(),
            _ => await ReadFileAsync(path),
        };

        if (text is not null && text.Length > MaxCharacters)
            throw new InvalidOperationException($"The HTML is too large ({text.Length:N0} characters). The limit is {MaxCharacters:N0}.");

        return text;
    }

    private static async Task<string> ReadFileAsync(string path)
    {
        if (new FileInfo(path) is { Exists: true, Length: > MaxCharacters * 4L })
            throw new InvalidOperationException($"'{path}' is too large. The limit is {MaxCharacters:N0} characters.");
        return await File.ReadAllTextAsync(path);
    }
}

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
        catch (Exception ex) { HandleError(ex); return 1; }
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
                AnsiConsole.MarkupLine($"[bold]Subject:[/] {Markup.Escape(preview.Subject ?? "")}");
                Console.WriteLine();
                Console.WriteLine(preview.HtmlContent);
                return 0;
            }

            EmailTemplate t = null!;
            await AnsiConsole.Status().Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching template...", async _ =>
                {
                    t = await client.GetEmailTemplateAsync(settings.TemplateType);
                });
            AnsiConsole.MarkupLine($"[bold]Type:[/] {Markup.Escape(t.TemplateType ?? "")}");
            AnsiConsole.MarkupLine($"[bold]Subject:[/] {Markup.Escape(t.Subject ?? "")}");
            Console.WriteLine();
            Console.WriteLine(t.Content);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── email templates update ────────────────────────────────────────────────────

public class EmailTemplateUpdateSettings : CommandSettings
{
    [CommandArgument(0, "<template-type>")]
    [Description("The template type to update")]
    public string TemplateType { get; set; } = string.Empty;

    [CommandOption("--subject <SUBJECT>")]
    [Description("New subject line")]
    public string? Subject { get; set; }

    [CommandOption("--content <PATH>")]
    [Description("Path to a file containing the new HTML/text content (use '-' for stdin)")]
    public string? ContentPath { get; set; }

    [CommandOption("--content-text <HTML>")]
    [Description("The new HTML/text content, passed inline")]
    public string? ContentText { get; set; }
}

public class EmailTemplateUpdateCommand : BaseCommand<EmailTemplateUpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmailTemplateUpdateSettings settings)
    {
        try
        {
            var newContent = await EmailInput.ReadAsync(settings.ContentPath, settings.ContentText, "--content", "--content-text");
            if (settings.Subject is null && newContent is null)
            {
                Renderer.Error("Nothing to update. Pass a new subject or new content.");
                return 1;
            }
            if (newContent is not null && string.IsNullOrWhiteSpace(newContent))
            {
                Renderer.Error("The new content is empty. Refusing to blank the template.");
                return 1;
            }

            var client = GetClient();
            var current = await client.GetEmailTemplateAsync(settings.TemplateType);
            var updated = await client.UpdateEmailTemplateAsync(settings.TemplateType,
                new UpdateEmailTemplateRequest(settings.Subject ?? current.Subject, newContent ?? current.Content));
            Renderer.Success($"Updated '{Markup.Escape(updated?.TemplateType ?? settings.TemplateType)}'.");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── email preview ─────────────────────────────────────────────────────────────

public class EmailPreviewRawSettings : CommandSettings
{
    [CommandOption("--subject <SUBJECT>")]
    [Description("Subject line to render (defaults to 'Preview')")]
    public string? Subject { get; set; }

    [CommandOption("--content <PATH>")]
    [Description("Path to file containing content (or '-' for stdin)")]
    public string? ContentPath { get; set; }

    [CommandOption("--content-text <HTML>")]
    [Description("The content to render, passed inline")]
    public string? ContentText { get; set; }

    [CommandOption("--wrapper <PATH>")]
    [Description("Optional path to a wrapper HTML override (or '-' for stdin)")]
    public string? WrapperPath { get; set; }

    [CommandOption("--wrapper-text <HTML>")]
    [Description("Optional wrapper HTML override, passed inline")]
    public string? WrapperText { get; set; }
}

public class EmailPreviewRawCommand : BaseCommand<EmailPreviewRawSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, EmailPreviewRawSettings settings)
    {
        try
        {
            if (settings.ContentPath == "-" && settings.WrapperPath == "-")
            {
                Renderer.Error("Only one of the content and the wrapper can be read from stdin.");
                return 1;
            }

            var content = await EmailInput.ReadAsync(settings.ContentPath, settings.ContentText, "--content", "--content-text") ?? string.Empty;
            var wrapper = await EmailInput.ReadAsync(settings.WrapperPath, settings.WrapperText, "--wrapper", "--wrapper-text");
            var client = GetClient();
            var preview = await client.PreviewRawEmailAsync(
                new PreviewRawEmailRequest(settings.Subject ?? "Preview", content, wrapper));
            Console.WriteLine(preview.HtmlContent);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

// ── email shell show / update ─────────────────────────────────────────────────

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
            Console.WriteLine();
            Console.WriteLine(shell.Html);
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}

public class EmailShellUpdateSettings : CommandSettings
{
    [CommandOption("--html <PATH>")]
    [Description("Path to file with the new shell HTML (or '-' for stdin)")]
    public string? HtmlPath { get; set; }

    [CommandOption("--html-text <HTML>")]
    [Description("The new shell HTML, passed inline")]
    public string? HtmlText { get; set; }

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
            var sources = new[] { settings.HtmlPath is not null, settings.HtmlText is not null, settings.Reset }.Count(x => x);
            if (sources != 1)
            {
                Renderer.Error("Pass exactly one of the shell HTML or --reset.");
                return 1;
            }

            var html = settings.Reset
                ? null
                : await EmailInput.ReadAsync(settings.HtmlPath, settings.HtmlText, "--html", "--html-text");
            if (!settings.Reset && string.IsNullOrWhiteSpace(html))
            {
                Renderer.Error("The shell HTML is empty. Use --reset to go back to the platform default.");
                return 1;
            }

            var client = GetClient();
            await client.UpdateEmailShellAsync(new UpdateEmailShellRequest(html));
            Renderer.Success(settings.Reset ? "Shell reset to default." : "Shell saved.");
            return 0;
        }
        catch (Exception ex) { HandleError(ex); return 1; }
    }
}
