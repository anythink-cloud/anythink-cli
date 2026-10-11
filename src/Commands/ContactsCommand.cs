using System.ComponentModel;
using System.Text.Json;
using AnythinkCli.Client;
using AnythinkCli.Contacts;
using AnythinkCli.Models;
using AnythinkCli.Output;
using Spectre.Console;
using Spectre.Console.Cli;

namespace AnythinkCli.Commands;

internal static class ContactsOutput
{
    public static void PrintJson(object value) =>
        Renderer.PrintJson(JsonSerializer.Serialize(value, JsonOutput.PrettyRelaxed));

    public const string UnavailableMessage = "Contacts aren't available on this project yet (the API returned 404).";

    // A missing contact comes back as a JSON error; a route the API doesn't have comes back bare.
    public static void HandleError(Exception ex)
    {
        if (ex is AnythinkException { StatusCode: 404 } ae && !IsJsonError(ae.Message))
            Renderer.Error(UnavailableMessage);
        else
            BaseCommand<ContactsListSettings>.HandleError(ex);
    }

    private static bool IsJsonError(string body)
    {
        try { return JsonDocument.Parse(body).RootElement.ValueKind == JsonValueKind.Object; }
        catch (JsonException) { return false; }
    }

    public static string Name(Contact c) =>
        string.Join(' ', new[] { c.FirstName, c.LastName }.Where(n => !string.IsNullOrWhiteSpace(n)));

    public static string When(DateTime? value) => value?.ToString("yyyy-MM-dd HH:mm") ?? "—";

    public static void Show(Contact c)
    {
        Renderer.KeyValue("ID", c.Id.ToString());
        Renderer.KeyValue("Email", c.Email);
        Renderer.KeyValue("Name", Name(c) is { Length: > 0 } n ? n : null);
        Renderer.KeyValue("Marketing status", c.EmailMarketingStatus);
        Renderer.KeyValue("Consent basis", c.ConsentBasis);
        Renderer.KeyValue("Status changed", c.StatusChangedAt is null ? null : When(c.StatusChangedAt));
        Renderer.KeyValue("Source", c.Source);
        Renderer.KeyValue("User", c.IsUser ? $"yes (user {c.UserId})" : "no");
        Renderer.KeyValue("Created", When(c.CreatedAt));
        Renderer.KeyValue("Updated", When(c.UpdatedAt));
    }

    // Typing the id makes erasing something you have to mean; --yes is for scripts.
    public static int? ConfirmErase(int id, bool yes, bool? interactive = null, Func<string?>? ask = null)
    {
        if (yes) return null;
        if (!(interactive ?? AnsiConsole.Profile.Capabilities.Interactive))
        {
            Renderer.Error("Confirmation required but this terminal is not interactive. Re-run with --yes to proceed.");
            return 1;
        }

        Renderer.Warn("Erasing a contact permanently removes their personal details and can't be undone.");
        var typed = ask is null
            ? AnsiConsole.Prompt(new TextPrompt<string>($"Type the contact id ([bold]{id}[/]) to confirm:").AllowEmpty())
            : ask();
        if (typed?.Trim() == id.ToString()) return null;

        Renderer.Info("Cancelled.");
        return 0;
    }
}

// ── contacts list ─────────────────────────────────────────────────────────────

public class ContactsListSettings : JsonSettings
{
    [CommandOption("--status <STATUS>")]
    [Description("Filter by marketing status: not_subscribed, pending, subscribed, unsubscribed, bounced or complained")]
    public string? Status { get; set; }

    [CommandOption("--is-user <BOOL>")]
    [Description("Only contacts who are (true) or aren't (false) users of the project")]
    public bool? IsUser { get; set; }

    [CommandOption("--source <SOURCE>")]
    [Description("Filter by where the contact came from")]
    public string? Source { get; set; }

    [CommandOption("--search <TEXT>")]
    [Description("Search by email or name")]
    public string? Search { get; set; }

    [CommandOption("--page <PAGE>")]
    [Description("Page number (default: 1)")]
    public int Page { get; set; } = 1;

    [CommandOption("--limit <LIMIT>")]
    [Description("Contacts per page (default: 25)")]
    public int Limit { get; set; } = 25;
}

public class ContactsListCommand : BaseCommand<ContactsListSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ContactsListSettings settings)
    {
        try
        {
            var client = GetClient();
            var result = await client.GetContactsAsync(
                settings.Status, settings.IsUser, settings.Source, settings.Search, settings.Page, settings.Limit);

            if (settings.Json)
            {
                ContactsOutput.PrintJson(result);
                return 0;
            }

            if (result.Items.Count == 0) { Renderer.Info("No contacts found."); return 0; }

            Renderer.Header($"Contacts ({result.TotalCount?.ToString() ?? result.Items.Count.ToString()})");
            var table = Renderer.BuildTable("ID", "Email", "Name", "Status", "Source", "User", "Created");
            foreach (var c in result.Items)
                Renderer.AddRow(table, c.Id.ToString(), c.Email, ContactsOutput.Name(c), c.EmailMarketingStatus, c.Source,
                    c.IsUser ? "yes" : "no", ContactsOutput.When(c.CreatedAt));
            AnsiConsole.Write(table);

            if (result.HasNextPage)
                Renderer.Info($"More contacts available: re-run with --page {settings.Page + 1}.");
            return 0;
        }
        catch (Exception ex) { ContactsOutput.HandleError(ex); return 1; }
    }
}

// ── contacts get ──────────────────────────────────────────────────────────────

public class ContactIdSettings : JsonSettings
{
    [CommandArgument(0, "<ID>")]
    [Description("Contact ID (a whole number)")]
    public int Id { get; set; }

    public override Spectre.Console.ValidationResult Validate() =>
        Id > 0
            ? Spectre.Console.ValidationResult.Success()
            : Spectre.Console.ValidationResult.Error("The contact ID must be a positive whole number.");
}

public class ContactsGetCommand : BaseCommand<ContactIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ContactIdSettings settings)
    {
        try
        {
            var contact = await GetClient().GetContactAsync(settings.Id);

            if (settings.Json)
            {
                ContactsOutput.PrintJson(contact);
                return 0;
            }

            Renderer.Header("Contact");
            ContactsOutput.Show(contact);

            if (contact.ConsentEvents is { Count: > 0 } events)
            {
                AnsiConsole.WriteLine();
                Renderer.Header($"Consent history ({events.Count})");
                var table = Renderer.BuildTable("When", "Action", "Basis", "Source", "Wording");
                foreach (var e in events)
                    Renderer.AddRow(table, ContactsOutput.When(e.OccurredAt), e.Action, e.Basis, e.Source, e.Wording);
                AnsiConsole.Write(table);
            }
            return 0;
        }
        catch (Exception ex) { ContactsOutput.HandleError(ex); return 1; }
    }
}

// ── contacts create ───────────────────────────────────────────────────────────

public class ContactsCreateSettings : JsonSettings
{
    [CommandArgument(0, "<EMAIL>")]
    [Description("The contact's email address")]
    public string Email { get; set; } = string.Empty;

    [CommandOption("--first-name <NAME>")]
    [Description("First name")]
    public string? FirstName { get; set; }

    [CommandOption("--last-name <NAME>")]
    [Description("Last name")]
    public string? LastName { get; set; }

    [CommandOption("--source <SOURCE>")]
    [Description("Where the contact came from, e.g. event or import")]
    public string? Source { get; set; }

    [CommandOption("--consent")]
    [Description("Record that the person agreed to marketing emails. Uses the project's default consent wording unless --wording is given")]
    public bool Consent { get; set; }

    [CommandOption("--wording <TEXT>")]
    [Description("The exact wording the person agreed to (needs --consent)")]
    public string? Wording { get; set; }

    [CommandOption("--consent-source <SOURCE>")]
    [Description("Where the consent was given, e.g. signup-form (needs --consent)")]
    public string? ConsentSource { get; set; }

    public override Spectre.Console.ValidationResult Validate() =>
        !Consent && (Wording is not null || ConsentSource is not null)
            ? Spectre.Console.ValidationResult.Error("--wording and --consent-source only apply with --consent.")
            : Spectre.Console.ValidationResult.Success();
}

public class ContactsCreateCommand : BaseCommand<ContactsCreateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ContactsCreateSettings settings)
    {
        try
        {
            var consent = settings.Consent ? new ContactConsentRequest(settings.Wording, settings.ConsentSource) : null;
            var contact = await GetClient().CreateContactAsync(new CreateContactRequest(
                settings.Email, settings.FirstName, settings.LastName, settings.Source, consent));

            if (settings.Json) { ContactsOutput.PrintJson(contact); return 0; }

            Renderer.Success($"Contact [#F97316]{contact.Id}[/] created.");
            ContactsOutput.Show(contact);
            return 0;
        }
        catch (Exception ex) { ContactsOutput.HandleError(ex); return 1; }
    }
}

// ── contacts update ───────────────────────────────────────────────────────────

public class ContactsUpdateSettings : ContactIdSettings
{
    [CommandOption("--first-name <NAME>")]
    [Description("New first name")]
    public string? FirstName { get; set; }

    [CommandOption("--last-name <NAME>")]
    [Description("New last name")]
    public string? LastName { get; set; }

    [CommandOption("--source <SOURCE>")]
    [Description("New source")]
    public string? Source { get; set; }

    public override Spectre.Console.ValidationResult Validate() =>
        !base.Validate().Successful ? base.Validate()
        : FirstName is null && LastName is null && Source is null
            ? Spectre.Console.ValidationResult.Error("Pass at least one of --first-name, --last-name or --source.")
            : Spectre.Console.ValidationResult.Success();
}

public class ContactsUpdateCommand : BaseCommand<ContactsUpdateSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ContactsUpdateSettings settings)
    {
        try
        {
            var contact = await GetClient().UpdateContactAsync(
                settings.Id, new UpdateContactRequest(settings.FirstName, settings.LastName, settings.Source));

            if (settings.Json) { ContactsOutput.PrintJson(contact); return 0; }

            Renderer.Success($"Contact [#F97316]{contact.Id}[/] updated.");
            ContactsOutput.Show(contact);
            return 0;
        }
        catch (Exception ex) { ContactsOutput.HandleError(ex); return 1; }
    }
}

// ── contacts unsubscribe ──────────────────────────────────────────────────────

public class ContactsUnsubscribeCommand : BaseCommand<ContactIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ContactIdSettings settings)
    {
        try
        {
            var contact = await GetClient().UnsubscribeContactAsync(settings.Id);

            if (settings.Json) { ContactsOutput.PrintJson(contact); return 0; }

            Renderer.Success($"Contact [#F97316]{contact.Id}[/] is now {Markup.Escape(contact.EmailMarketingStatus ?? "unsubscribed")}.");
            return 0;
        }
        catch (Exception ex) { ContactsOutput.HandleError(ex); return 1; }
    }
}

// ── contacts erase ────────────────────────────────────────────────────────────

public class ContactsEraseSettings : ContactIdSettings
{
    [CommandOption("--yes")]
    [Description("Skip the confirmation prompt")]
    public bool Yes { get; set; }
}

public class ContactsEraseCommand : BaseCommand<ContactsEraseSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ContactsEraseSettings settings)
    {
        try
        {
            if (ContactsOutput.ConfirmErase(settings.Id, settings.Yes) is { } exit)
                return exit;

            await GetClient().EraseContactAsync(settings.Id);
            if (settings.Json) { ContactsOutput.PrintJson(new { erased = settings.Id }); return 0; }
            Renderer.Success($"Contact [#F97316]{settings.Id}[/] erased.");
            return 0;
        }
        catch (Exception ex) { ContactsOutput.HandleError(ex); return 1; }
    }
}

// ── contacts export ───────────────────────────────────────────────────────────

public class ContactsExportCommand : BaseCommand<ContactIdSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ContactIdSettings settings)
    {
        try
        {
            var export = await GetClient().ExportContactAsync(settings.Id);
            Renderer.PrintJson(export.ToJsonString(JsonOutput.PrettyRelaxed));
            return 0;
        }
        catch (Exception ex) { ContactsOutput.HandleError(ex); return 1; }
    }
}

// ── contacts subscribe ────────────────────────────────────────────────────────

public class ContactsSubscribeSettings : JsonSettings
{
    [CommandArgument(0, "<EMAIL>")]
    [Description("The email address signing up")]
    public string Email { get; set; } = string.Empty;

    [CommandOption("--first-name <NAME>")]
    [Description("First name")]
    public string? FirstName { get; set; }

    [CommandOption("--last-name <NAME>")]
    [Description("Last name")]
    public string? LastName { get; set; }

    [CommandOption("--source <SOURCE>")]
    [Description("Where the signup came from (default: cli)")]
    public string? Source { get; set; }

    [CommandOption("--wording <TEXT>")]
    [Description("The wording shown next to the form. Defaults to the project's consent wording")]
    public string? Wording { get; set; }
}

public class ContactsSubscribeCommand : BaseCommand<ContactsSubscribeSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, ContactsSubscribeSettings settings)
    {
        try
        {
            const string warning = "This records consent as if the person filled in the signup form. It is meant for testing with your own address.";
            if (settings.Json) await Console.Error.WriteLineAsync(warning);
            else Renderer.Warn(warning);

            var result = await GetClient().SubscribeAsync(new SubscribeRequest(
                settings.Email, settings.FirstName, settings.LastName, settings.Source ?? "cli", settings.Wording));

            if (settings.Json) { ContactsOutput.PrintJson(result ?? new SubscribeResult("accepted")); return 0; }

            Renderer.Success("Signup accepted. With double opt-in on, a confirmation email goes to the address before they're subscribed.");
            return 0;
        }
        catch (Exception ex) { ContactsOutput.HandleError(ex); return 1; }
    }
}

// ── contacts import ───────────────────────────────────────────────────────────

public class ContactsImportSettings : JsonSettings
{
    [CommandArgument(0, "[FILE]")]
    [Description("CSV file with the columns email, status and optionally first_name, last_name, source, consented_at, wording, ip_address")]
    public string? File { get; set; }

    [CommandOption("--csv-text <CSV>")]
    [Description("The CSV passed inline instead of a file (header row first)")]
    public string? CsvText { get; set; }

    [CommandOption("--dry-run")]
    [Description("Check the file and report problems without sending anything")]
    public bool DryRun { get; set; }

    [CommandOption("--skip-invalid")]
    [Description("Import the valid rows even if some rows have problems (by default nothing is sent until every row is valid)")]
    public bool SkipInvalid { get; set; }

    public override Spectre.Console.ValidationResult Validate() =>
        (File is null) == (CsvText is null)
            ? Spectre.Console.ValidationResult.Error("Pass either a CSV file or --csv-text.")
            : Spectre.Console.ValidationResult.Success();
}

public class ContactsImportCommand : BaseCommand<ContactsImportSettings>
{
    private const int MaxIssuesShown = 25;
    private const long MaxFileBytes = 50L * 1024 * 1024;

    public override async Task<int> ExecuteAsync(CommandContext context, ContactsImportSettings settings)
    {
        try
        {
            using var reader = Open(settings);
            var parsed = ContactsImport.Parse(reader);

            if (parsed.IgnoredColumns.Count > 0)
                Renderer.Info($"Ignoring columns that aren't contact fields: {Markup.Escape(string.Join(", ", parsed.IgnoredColumns))}.");

            if (parsed.Issues.Count > 0)
            {
                Renderer.Warn($"{parsed.Issues.Count} row{(parsed.Issues.Count == 1 ? " has" : "s have")} problems (row 1 is the header):");
                foreach (var issue in parsed.Issues.Take(MaxIssuesShown))
                    AnsiConsole.MarkupLine($"  row {issue.Row}: {Markup.Escape(issue.Problem)}");
                if (parsed.Issues.Count > MaxIssuesShown)
                    AnsiConsole.MarkupLine($"  and {parsed.Issues.Count - MaxIssuesShown} more.");

                if (!settings.SkipInvalid && !settings.DryRun)
                {
                    Renderer.Error("Nothing was imported. Fix those rows, or pass --skip-invalid to import the rest.");
                    return 1;
                }
            }

            if (settings.DryRun)
            {
                Renderer.Info($"{parsed.Rows.Count} row{(parsed.Rows.Count == 1 ? "" : "s")} ready to import. Nothing was sent.");
                return parsed.Issues.Count > 0 ? 1 : 0;
            }

            if (parsed.Rows.Count == 0)
            {
                Renderer.Error("There are no valid rows to import.");
                return 1;
            }

            var summary = await ContactsImport.SendAsync(GetClient(), parsed.Rows, parsed.Issues.Count);

            if (settings.Json) ContactsOutput.PrintJson(summary);
            else Print(summary);

            return summary.FailedStatus is null ? 0 : 1;
        }
        catch (Exception ex) { ContactsOutput.HandleError(ex); return 1; }
    }

    private static TextReader Open(ContactsImportSettings settings)
    {
        if (settings.CsvText is not null) return new StringReader(settings.CsvText);

        if (ClientContext.Remote)
            throw new InvalidOperationException("A file path reads a file on this machine and can't be used remotely. Pass csv_text instead.");

        var info = new FileInfo(settings.File!);
        if (!info.Exists) throw new CliException($"File not found: {Markup.Escape(settings.File!)}");
        if (info.Length > MaxFileBytes) throw new CliException("The file is larger than 50 MB. Split it and import the parts.");
        return new StreamReader(info.FullName);
    }

    private static void Print(ContactsImportSummary s)
    {
        if (s.FailedStatus is { } status)
        {
            var reason = s.FailedMessage is { Length: > 0 } m ? $": {m}" : "";
            Renderer.Error($"The import stopped after {s.ChunksCompleted} of {s.Chunks} batches (the API returned status {status}{reason}). Importing again is safe; existing contacts are updated, not duplicated.");
        }
        else
            Renderer.Success("Import complete.");

        Renderer.KeyValue("Created", s.Created.ToString());
        Renderer.KeyValue("Updated", s.Updated.ToString());
        Renderer.KeyValue("Suppressed", s.Suppressed.ToString());
        Renderer.KeyValue("Invalid", s.Invalid.ToString());
    }
}
