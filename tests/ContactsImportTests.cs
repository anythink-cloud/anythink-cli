using System.Net;
using AnythinkCli.Client;
using AnythinkCli.Commands;
using AnythinkCli.Contacts;
using AnythinkCli.Models;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class ContactsImportTests
{
    private const string Org = "https://api.example.com/org/99999";

    private static ParsedContacts Parse(string csv) => ContactsImport.Parse(new StringReader(csv));

    private static List<ImportContactRow> Rows(int count) =>
        Enumerable.Range(0, count).Select(i => new ImportContactRow($"person{i}@example.com", "subscribed")).ToList();

    // ── Rule: the header must carry email and status; other columns are optional ──

    [Fact]
    public void Parse_ReadsEveryColumnIntoItsField()
    {
        var parsed = Parse("""
            email,first_name,last_name,source,status,consented_at,wording,ip_address
            ann@example.com,Ann,Lee,mailchimp,subscribed,2025-03-01T10:00:00Z,"Yes, please",203.0.113.9
            """);

        parsed.Issues.Should().BeEmpty();
        parsed.Rows.Should().ContainSingle().Which.Should().Be(new ImportContactRow(
            "ann@example.com", "subscribed", "Ann", "Lee", "mailchimp", "2025-03-01T10:00:00Z", "Yes, please", "203.0.113.9"));
    }

    [Fact]
    public void Parse_HeaderNamesAreCaseSpaceAndBomInsensitive()
    {
        var parsed = Parse("﻿Email, Status ,First Name\nann@example.com,unsubscribed,Ann\n");

        parsed.Rows.Should().ContainSingle().Which.Should().Be(new ImportContactRow("ann@example.com", "unsubscribed", "Ann"));
    }

    [Theory]
    [InlineData("email,first_name\nann@example.com,Ann\n", "status")]
    [InlineData("status\nsubscribed\n", "email")]
    public void Parse_MissingRequiredColumn_NamesIt(string csv, string column)
    {
        var act = () => Parse(csv);

        act.Should().Throw<CliException>().WithMessage($"*{column}*");
    }

    [Fact]
    public void Parse_EmptyFile_Throws() =>
        ((Action)(() => Parse(""))).Should().Throw<CliException>();

    [Fact]
    public void Parse_UnknownColumns_AreIgnoredAndReported()
    {
        var parsed = Parse("email,status,favourite_colour\nann@example.com,subscribed,green\n");

        parsed.IgnoredColumns.Should().Equal("favourite_colour");
        parsed.Rows.Should().ContainSingle();
    }

    [Fact]
    public void Parse_ProviderStatusesAreSentAsTheyAre() =>
        Parse("email,status\na@example.com,unsubscribed\nb@example.com,cleaned\n").Rows
            .Select(r => r.Status).Should().Equal("unsubscribed", "cleaned");

    // ── Rule: bad rows are found locally and reported by row number, header being row 1 ──

    [Fact]
    public void Parse_InvalidRows_AreReportedByRowNumber_AndLeftOut()
    {
        var parsed = Parse("""
            email,status,consented_at,ip_address
            ok@example.com,subscribed,,
            not-an-email,subscribed,,
            nostatus@example.com,,,
            baddate@example.com,subscribed,yesterday-ish,
            badip@example.com,subscribed,,999.1.1.1
            """);

        parsed.Rows.Should().ContainSingle();
        parsed.Issues.Select(i => i.Row).Should().Equal(3, 4, 5, 6);
    }

    [Fact]
    public void Parse_BlankLines_AreSkippedButStillCounted()
    {
        var parsed = Parse("email,status\n\nbad,subscribed\n");

        parsed.Issues.Should().ContainSingle().Which.Row.Should().Be(3);
    }

    [Fact]
    public void Parse_IssueMessages_NeverContainTheValues()
    {
        var parsed = Parse("email,status,consented_at,ip_address\nsecret-person@example,subscribed,,\nfine@example.com,subscribed,lastweek,\nfine2@example.com,subscribed,,not-an-ip\n");

        parsed.Issues.Should().HaveCount(3);
        parsed.Issues.Select(i => i.Problem).Should().NotContain(p => p.Contains("secret") || p.Contains("lastweek") || p.Contains("not-an-ip") || p.Contains("fine"));
    }

    // ── Rule: rows go in batches of at most 1,000 ────────────────────────────

    [Theory]
    [InlineData(1, new[] { 1 })]
    [InlineData(1000, new[] { 1000 })]
    [InlineData(1001, new[] { 1000, 1 })]
    [InlineData(2500, new[] { 1000, 1000, 500 })]
    public void Chunk_SplitsIntoBatchesOfAtMostOneThousand(int count, int[] expected) =>
        ContactsImport.Chunk(Rows(count)).Select(c => c.Count).Should().Equal(expected);

    [Fact]
    public async Task SendAsync_SendsEachBatchAndSumsTheResults()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Post, $"{Org}/contacts/import").Respond("application/json", """{"created":900,"updated":50,"suppressed":40,"invalid":10}""");
        handler.Expect(HttpMethod.Post, $"{Org}/contacts/import").Respond("application/json", """{"created":1,"updated":0,"suppressed":0,"invalid":0}""");

        var summary = await ContactsImport.SendAsync(CommandRunner.ClientFor(handler), Rows(1001), localInvalid: 2);

        summary.Should().Be(new ContactsImportSummary(1001, 901, 50, 40, 12, 2, 2, null));
        handler.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task SendAsync_WhenABatchFails_StopsAndKeepsTheTotalsSoFar()
    {
        var handler = new MockHttpMessageHandler();
        handler.Expect(HttpMethod.Post, $"{Org}/contacts/import").Respond("application/json", """{"created":1000,"updated":0,"suppressed":0,"invalid":0}""");
        handler.Expect(HttpMethod.Post, $"{Org}/contacts/import").Respond(HttpStatusCode.ServiceUnavailable);

        var summary = await ContactsImport.SendAsync(CommandRunner.ClientFor(handler), Rows(2500));

        summary.Created.Should().Be(1000);
        summary.ChunksCompleted.Should().Be(1);
        summary.Chunks.Should().Be(3);
        summary.FailedStatus.Should().Be(503);
    }

    // ── Rule: the command never prints an email address ──────────────────────

    [Fact]
    public async Task Import_Output_NeverContainsAnEmailAddress_ForBadRowsOrTheSummary()
    {
        var csv = "email,status\nrow-one@example.com,subscribed\nbroken-address,subscribed\n";
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{Org}/contacts/import").Respond("application/json", """{"created":1,"updated":0,"suppressed":0,"invalid":0}""");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsImportCommand().ExecuteAsync(null!, new ContactsImportSettings { CsvText = csv, SkipInvalid = true }));

        code.Should().Be(0);
        output.Should().Contain("row 3").And.Contain("Created");
        output.Should().NotContain("row-one").And.NotContain("broken-address").And.NotContain("@");
    }

    [Fact]
    public async Task Import_WithInvalidRows_SendsNothingUnlessSkipInvalidIsGiven()
    {
        var handler = new MockHttpMessageHandler();
        var posts = 0;
        handler.When(HttpMethod.Post, $"{Org}/contacts/import").Respond(_ => { posts++; return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"created":1,"updated":0,"suppressed":0,"invalid":0}""") }; });

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsImportCommand().ExecuteAsync(null!, new ContactsImportSettings { CsvText = "email,status\nok@example.com,subscribed\nbad,subscribed\n" }));

        code.Should().Be(1);
        posts.Should().Be(0);
        output.Should().Contain("Nothing was imported").And.NotContain("@");
    }

    [Fact]
    public async Task Import_DryRun_SendsNothing()
    {
        var handler = new MockHttpMessageHandler();

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsImportCommand().ExecuteAsync(null!, new ContactsImportSettings { CsvText = "email,status\nok@example.com,subscribed\n", DryRun = true }));

        code.Should().Be(0);
        output.Should().Contain("1 row ready").And.Contain("Nothing was sent");
    }

    [Fact]
    public async Task Import_FailedBatch_ReportsOnlyTheStatus()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{Org}/contacts/import").Respond(HttpStatusCode.BadRequest, "application/json", """{"error":"bad row for leaked@example.com"}""");

        var (code, output) = await CommandRunner.RunAsync(handler, () =>
            new ContactsImportCommand().ExecuteAsync(null!, new ContactsImportSettings { CsvText = "email,status\nleaked@example.com,subscribed\n" }));

        code.Should().Be(1);
        output.Should().Contain("400").And.NotContain("leaked");
    }

    [Fact]
    public void ImportSettings_NeedExactlyOneOfFileOrCsvText()
    {
        new ContactsImportSettings().Validate().Successful.Should().BeFalse();
        new ContactsImportSettings { File = "a.csv", CsvText = "x" }.Validate().Successful.Should().BeFalse();
        new ContactsImportSettings { File = "a.csv" }.Validate().Successful.Should().BeTrue();
    }
}
