using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;
using System.ComponentModel;

namespace AnythinkCli.Commands;

// ── files list ────────────────────────────────────────────────────────────────

public class FilesListSettings : CommandSettings
{
    [CommandOption("--page <PAGE>")]
    [Description("Page number (default: 1)")]
    public int Page { get; set; } = 1;

    [CommandOption("--limit <LIMIT>")]
    [Description("Items per page (default: 25)")]
    public int Limit { get; set; } = 25;
}

public class FilesListCommand : BaseCommand<FilesListSettings>
{
    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_048_576)
            return $"{bytes / 1_048_576.0:0.#} MB";
        if (bytes >= 1_024)
            return $"{bytes / 1_024.0:0.#} KB";
        return $"{bytes} B";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, FilesListSettings settings)
    {
        try
        {
            var client = GetClient();
            List<FileResponse> files = [];

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Fetching files...", async _ =>
                {
                    files = await client.GetFilesAsync(settings.Page, settings.Limit);
                });

            if (files.Count == 0)
            {
                Renderer.Info("No files found.");
                return 0;
            }

            Renderer.Header($"Files (page {settings.Page})");

            var table = Renderer.BuildTable("ID", "Name", "Type", "Size", "Public", "Created");
            foreach (var f in files)
            {
                Renderer.AddRow(table,
                    f.Id.ToString(),
                    f.OriginalFileName,
                    f.FileType,
                    FormatSize(f.FileSize),
                    f.IsPublic ? "yes" : "no",
                    f.CreatedAt.ToString("yyyy-MM-dd HH:mm")
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

// ── files get ─────────────────────────────────────────────────────────────────

public class FileGetSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("File ID")]
    public int Id { get; set; }
}

public class FilesGetCommand : BaseCommand<FileGetSettings>
{
    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_048_576)
            return $"{bytes / 1_048_576.0:0.#} MB";
        if (bytes >= 1_024)
            return $"{bytes / 1_024.0:0.#} KB";
        return $"{bytes} B";
    }

    public override async Task<int> ExecuteAsync(CommandContext context, FileGetSettings settings)
    {
        try
        {
            var client = GetClient();
            FileResponse? file = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Fetching file {settings.Id}...", async _ =>
                {
                    file = await client.GetFileAsync(settings.Id);
                });

            if (file == null)
            {
                Renderer.Error($"File {settings.Id} not found.");
                return 1;
            }

            Renderer.Header($"File: {file.OriginalFileName}");
            Renderer.KeyValue("ID", file.Id.ToString());
            Renderer.KeyValue("Original name", file.OriginalFileName);
            Renderer.KeyValue("Stored name", file.FileName);
            Renderer.KeyValue("Type", file.FileType);
            Renderer.KeyValue("Size", FormatSize(file.FileSize));
            Renderer.KeyValue("Public", file.IsPublic ? "yes" : "no");
            Renderer.KeyValue("Created", file.CreatedAt.ToString("yyyy-MM-dd HH:mm"));
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── files upload ──────────────────────────────────────────────────────────────

public class FileUploadSettings : CommandSettings
{
    [CommandArgument(0, "<PATH>")]
    [Description("Path to the file to upload")]
    public string FilePath { get; set; } = "";

    [CommandOption("--public")]
    [Description("Make the file publicly accessible (default: false)")]
    public bool IsPublic { get; set; }
}

public class FilesUploadCommand : BaseCommand<FileUploadSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, FileUploadSettings settings)
    {
        if (!File.Exists(settings.FilePath))
        {
            Renderer.Error($"File not found: {settings.FilePath}");
            return 1;
        }

        try
        {
            var client = GetClient();
            FileResponse? file = null;

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Uploading...", async _ =>
                {
                    file = await client.UploadFileAsync(settings.FilePath, settings.IsPublic);
                });

            Renderer.Success($"File uploaded: [#F97316]{Markup.Escape(file!.OriginalFileName)}[/]");
            Renderer.KeyValue("ID", file.Id.ToString());
            Renderer.KeyValue("Stored as", file.FileName);
            Renderer.KeyValue("Public", file.IsPublic ? "yes" : "no");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}

// ── files delete ──────────────────────────────────────────────────────────────

public class FileDeleteSettings : CommandSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("File ID to delete")]
    public int Id { get; set; }

    [CommandOption("--yes")]
    [Description("Skip confirmation prompt")]
    public bool Yes { get; set; }
}

public class FilesDeleteCommand : BaseCommand<FileDeleteSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, FileDeleteSettings settings)
    {
        if (!settings.Yes)
        {
            var confirm = AnsiConsole.Confirm(
                $"[yellow]Delete file[/] [bold red]{settings.Id}[/][yellow]?[/]",
                defaultValue: false);
            if (!confirm)
            {
                Renderer.Info("Cancelled.");
                return 0;
            }
        }

        try
        {
            var client = GetClient();

            await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync($"Deleting file {settings.Id}...", async _ =>
                {
                    await client.DeleteFileAsync(settings.Id);
                });

            Renderer.Success($"File [#F97316]{settings.Id}[/] deleted.");
            return 0;
        }
        catch (Exception ex)
        {
            HandleError(ex);
            return 1;
        }
    }
}
