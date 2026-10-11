using System.Net;
using AnythinkCli.Client;
using AnythinkCli.DataTransfer;
using AnythinkCli.Models;

namespace AnythinkCli.Contacts;

internal sealed record ContactRowIssue(int Row, string Problem);

internal sealed record ParsedContacts(
    List<ImportContactRow> Rows,
    List<ContactRowIssue> Issues,
    List<string> IgnoredColumns);

internal sealed record ContactsImportSummary(
    int Sent, int Created, int Updated, int Suppressed, int Invalid, int Chunks, int ChunksCompleted, int? FailedStatus);

internal static class ContactsImport
{
    public const int MaxRowsPerCall = 1000;

    private static readonly string[] Known =
        ["email", "first_name", "last_name", "source", "status", "consented_at", "wording", "ip_address"];

    // Row numbers count the header as row 1, so they match what a spreadsheet shows.
    public static ParsedContacts Parse(TextReader reader)
    {
        var records = Csv.ReadRecords(reader).GetEnumerator();
        if (!records.MoveNext())
            throw new CliException("The file is empty. It needs a header row with at least email and status.");

        var header = records.Current.Select(Normalise).ToList();
        if (header.Count > 0 && header[0].Length > 0 && header[0][0] == '﻿')
            header[0] = header[0].TrimStart('﻿');

        var index = new Dictionary<string, int>();
        for (var i = 0; i < header.Count; i++)
            if (Known.Contains(header[i]) && !index.ContainsKey(header[i])) index[header[i]] = i;

        var missing = new[] { "email", "status" }.Where(c => !index.ContainsKey(c)).ToList();
        if (missing.Count > 0)
            throw new CliException(Spectre.Console.Markup.Escape($"The header row is missing the required column{(missing.Count > 1 ? "s" : "")}: {string.Join(", ", missing)}."));

        var ignored = header.Where(h => h.Length > 0 && !Known.Contains(h)).Distinct().ToList();
        var rows = new List<ImportContactRow>();
        var issues = new List<ContactRowIssue>();

        var rowNumber = 1;
        while (records.MoveNext())
        {
            rowNumber++;
            var record = records.Current;
            if (record.All(string.IsNullOrWhiteSpace)) continue;

            string? Cell(string column) =>
                index.TryGetValue(column, out var i) && i < record.Count && record[i].Trim() is { Length: > 0 } v ? v : null;

            var email = Cell("email");
            var status = Cell("status");
            var consentedAt = Cell("consented_at");
            var ip = Cell("ip_address");

            var problem =
                email is null ? "email is missing"
                : !LooksLikeEmail(email) ? "email isn't a valid address"
                : status is null ? "status is missing"
                : consentedAt is not null && !DateTimeOffset.TryParse(consentedAt, out _) ? "consented_at isn't a date"
                : ip is not null && !IPAddress.TryParse(ip, out _) ? "ip_address isn't an IP address"
                : null;

            if (problem is not null)
            {
                issues.Add(new ContactRowIssue(rowNumber, problem));
                continue;
            }

            rows.Add(new ImportContactRow(
                email!, status!, Cell("first_name"), Cell("last_name"), Cell("source"), consentedAt, Cell("wording"), ip));
        }

        return new ParsedContacts(rows, issues, ignored);
    }

    public static IEnumerable<List<ImportContactRow>> Chunk(IReadOnlyList<ImportContactRow> rows)
    {
        for (var i = 0; i < rows.Count; i += MaxRowsPerCall)
            yield return [.. rows.Skip(i).Take(MaxRowsPerCall)];
    }

    // A failed chunk stops the import with the totals so far; the error carries the status only, as the API may echo an address.
    public static async Task<ContactsImportSummary> SendAsync(AnythinkClient client, IReadOnlyList<ImportContactRow> rows, int localInvalid = 0)
    {
        var chunks = Chunk(rows).ToList();
        int created = 0, updated = 0, suppressed = 0, invalid = localInvalid, done = 0, sent = 0;
        int? failedStatus = null;

        foreach (var chunk in chunks)
        {
            try
            {
                var result = await client.ImportContactsAsync(chunk);
                created += result.Created;
                updated += result.Updated;
                suppressed += result.Suppressed;
                invalid += result.Invalid;
                sent += chunk.Count;
                done++;
            }
            catch (AnythinkException ex)
            {
                failedStatus = ex.StatusCode;
                break;
            }
        }

        return new ContactsImportSummary(sent, created, updated, suppressed, invalid, chunks.Count, done, failedStatus);
    }

    public static bool LooksLikeEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1
            && value[(at + 1)..].Contains('.') && !value.Any(char.IsWhiteSpace);
    }

    private static string Normalise(string name) =>
        name.Trim().TrimStart('﻿').ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
}
