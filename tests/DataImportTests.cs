using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using AnythinkCli.Client;
using AnythinkCli.DataTransfer;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class DataImportTests
{
    private static readonly Func<TimeSpan, CancellationToken, Task> NoDelay = (_, _) => Task.CompletedTask;

    private static async Task<(DataImportResult Result, FakeApi Api)> Import(
        string content, string fileName = "in.csv", Action<FakeApi>? setup = null, Func<DataImportOptions, DataImportOptions>? tweak = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        using var dir = new TempDir();
        var api = new FakeApi();
        setup?.Invoke(api);
        var options = new DataImportOptions("customers", dir.File(fileName, content), Yes: true, Concurrency: 1);
        var result = await new DataImportRunner(api.Client(), delay ?? NoDelay).RunAsync(tweak?.Invoke(options) ?? options);
        return (result, api);
    }

    private static JsonObject Body(FakeApi api, int i = 0) => JsonNode.Parse(api.Posted[i])!.AsObject();

    // ── Coercion ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_CsvLeadingZeroInTextField_IsKeptAsText()
    {
        var (_, api) = await Import("name,zip\nAda,00123\n");
        Body(api)["zip"]!.GetValue<string>().Should().Be("00123");
    }

    [Fact]
    public async Task Import_DecimalWithDot_ParsedWithInvariantCultureWithoutRounding()
    {
        var previous = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            var (_, api) = await Import("name,price\nAda,19.9900000000000000001\n");
            Body(api)["price"]!.GetValue<string>().Should().Be("19.9900000000000000001");
        }
        finally { Thread.CurrentThread.CurrentCulture = previous; }
    }

    [Fact]
    public async Task Import_DecimalWithCommaSeparator_IsRejectedNotGuessed()
    {
        var (result, api) = await Import("name,price\nAda,\"1,5\"\n");
        api.Posted.Should().BeEmpty();
        result.Skipped.Should().Be(1);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("FALSE", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("yes", true)]
    [InlineData("No", false)]
    public async Task Import_BooleanVariants_AreAccepted(string cell, bool expected)
    {
        var (_, api) = await Import($"name,active\nAda,{cell}\n");
        Body(api)["active"]!.GetValue<bool>().Should().Be(expected);
    }

    [Fact]
    public async Task Import_BooleanGarbage_IsRejected()
    {
        var (result, api) = await Import("name,active\nAda,maybe\n");
        api.Posted.Should().BeEmpty();
        result.Problems.Should().ContainSingle().Which.Should().Contain("active");
    }

    [Fact]
    public async Task Import_JsonbCell_IsParsedAndSentAsJsonText()
    {
        var (_, api) = await Import("name,meta\nAda,\"{\"\"tags\"\": [1, 2]}\"\n");
        Body(api)["meta"]!.GetValue<string>().Should().Be("{\"tags\":[1,2]}");
    }

    [Fact]
    public async Task Import_JsonbCellThatIsNotJson_IsRejected()
    {
        var (result, api) = await Import("name,meta\nAda,{oops\n");
        api.Posted.Should().BeEmpty();
        result.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task Import_EmptyCsvCell_BecomesNull()
    {
        var (_, api) = await Import("name,age\nAda,\n");
        Body(api)["age"].Should().BeNull();
        Body(api).ContainsKey("age").Should().BeTrue();
    }

    [Fact]
    public async Task Import_EmptyJsonString_IsKeptAsEmptyText()
    {
        var (_, api) = await Import("{\"name\":\"\",\"zip\":\"x\"}\n", "in.jsonl");
        Body(api)["name"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public async Task Import_IsoDates_AreSentAsIso()
    {
        var (_, api) = await Import("name,born\nAda,1990-02-03\n");
        Body(api)["born"]!.GetValue<string>().Should().Be("1990-02-03");
    }

    [Fact]
    public async Task Import_AmbiguousSlashDate_IsRejected()
    {
        var (result, _) = await Import("name,born\nAda,03/02/1990\n");
        result.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task Import_UnknownColumn_RejectsRowUnlessIgnored()
    {
        var (strict, api) = await Import("name,nope\nAda,1\n");
        strict.Skipped.Should().Be(1);
        api.Posted.Should().BeEmpty();

        var (lenient, api2) = await Import("name,nope\nAda,1\n", tweak: o => o with { IgnoreUnknown = true });
        lenient.Created.Should().Be(1);
        Body(api2).ContainsKey("nope").Should().BeFalse();
    }

    [Fact]
    public async Task Import_JsonNumberIntoTextField_IsKeptLosslessly()
    {
        var (_, api) = await Import("{\"name\":123.50}\n", "in.jsonl");
        Body(api)["name"]!.GetValue<string>().Should().Be("123.50");
    }

    // ── Readers ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_PrettyPrintedMultiLineJsonArray_IsStreamedRowByRow()
    {
        var (result, api) = await Import("[\n  {\n    \"name\": \"Ada\",\n    \"age\": 36\n  },\n  {\n    \"name\": \"Bo\"\n  }\n]", "in.json");
        result.Created.Should().Be(2);
        Body(api, 1)["name"]!.GetValue<string>().Should().Be("Bo");
    }

    [Fact]
    public async Task Import_Jsonl_ReadsOneObjectPerLineAndIgnoresBlankLines()
    {
        var (result, _) = await Import("{\"name\":\"Ada\"}\n\n{\"name\":\"Bo\"}\n", "in.jsonl");
        result.Created.Should().Be(2);
    }

    [Fact]
    public async Task Import_UnknownExtension_FormatSniffedFromFirstCharacter()
    {
        var (arr, _) = await Import("  [{\"name\":\"Ada\"}]", "data.dat");
        var (lines, _) = await Import("{\"name\":\"Ada\"}\n", "data.dat");
        arr.Created.Should().Be(1);
        lines.Created.Should().Be(1);
    }

    [Fact]
    public async Task Import_CsvQuotedMultiLineCellWithEmbeddedQuotes_IsOneRecord()
    {
        var (result, api) = await Import("name,zip\r\n\"Ada \"\"the\"\" first\nline two\",1\r\n");
        result.Created.Should().Be(1);
        Body(api)["name"]!.GetValue<string>().Should().Be("Ada \"the\" first\nline two");
    }

    // ── Bad rows ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_MalformedRow_IsSkippedWithRowNumberAndNeverPosted()
    {
        var (result, api) = await Import("name,age\nAda,36\nBo,notanumber\nCy,40\n");

        result.Created.Should().Be(2);
        result.Skipped.Should().Be(1);
        result.Problems.Should().ContainSingle().Which.Should().StartWith("row 3:");
        api.Posted.Should().HaveCount(2).And.NotContain(p => p.Contains("Bo"));
        result.HasProblems.Should().BeTrue();
    }

    [Fact]
    public async Task Import_RowWithNoValues_IsNeverPostedAsEmptyObject()
    {
        var (result, api) = await Import("{}\n{\"name\":null}\n", "in.jsonl");
        api.Posted.Should().BeEmpty();
        result.Skipped.Should().Be(2);
    }

    [Fact]
    public async Task Import_CsvRowWithWrongCellCount_IsSkipped()
    {
        var (result, api) = await Import("name,age\nAda\nBo,3\n");
        result.Skipped.Should().Be(1);
        api.Posted.Should().ContainSingle();
    }

    [Fact]
    public async Task Import_UnterminatedQuote_StopsReadingAndReportsTheRow()
    {
        var (result, _) = await Import("name,age\nAda,1\n\"Bo,2\n");
        result.Created.Should().Be(1);
        result.Problems.Should().ContainSingle().Which.Should().Contain("never closed").And.NotContain("Row");
    }

    [Fact]
    public async Task Import_ErrorsFile_HoldsOriginalValuesPlusErrorColumn()
    {
        using var dir = new TempDir();
        var errors = dir.File("bad.csv");
        var file = dir.File("in.csv", "name,age\nAda,36\nBo,x\n");
        var api = new FakeApi();

        await new DataImportRunner(api.Client(), NoDelay).RunAsync(new DataImportOptions("customers", file, Yes: true, ErrorsPath: errors));

        var lines = File.ReadAllLines(errors);
        lines[0].Should().Be("name,age,_error");
        lines[1].Should().StartWith("Bo,x,");
        lines.Should().HaveCount(2);
    }

    [Fact]
    public async Task Import_ErrorsFileForJsonl_AddsErrorProperty()
    {
        using var dir = new TempDir();
        var errors = dir.File("bad.jsonl");
        var file = dir.File("in.jsonl", "{\"name\":\"Ada\",\"age\":\"x\"}\n");

        await new DataImportRunner(new FakeApi().Client(), NoDelay).RunAsync(new DataImportOptions("customers", file, Yes: true, ErrorsPath: errors));

        var row = JsonNode.Parse(File.ReadAllLines(errors)[0])!.AsObject();
        row["age"]!.GetValue<string>().Should().Be("x");
        row["_error"]!.GetValue<string>().Should().Contain("age");
    }

    // ── Writes, retries, concurrency ─────────────────────────────────────────

    [Fact]
    public async Task Import_Http400_IsNotRetriedAndCountedAsFailed()
    {
        var (result, api) = await Import("name\nAda\n", setup: a => a.OnPost = _ => FakeApi.Json(HttpStatusCode.BadRequest, "{\"error\":\"nope\"}"));

        api.Posted.Should().ContainSingle();
        result.Failed.Should().Be(1);
        result.Created.Should().Be(0);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Import_Http409And422_AreNeverRetried(HttpStatusCode code)
    {
        var (_, api) = await Import("name\nAda\n", setup: a => a.OnPost = _ => FakeApi.Json(code, "{}"));
        api.Posted.Should().ContainSingle();
    }

    [Fact]
    public async Task Import_Http429_IsRetriedWithGrowingBackoff()
    {
        var delays = new List<TimeSpan>();
        var (result, api) = await Import("name\nAda\n",
            setup: a => a.OnPost = n => n < 3 ? FakeApi.Json((HttpStatusCode)429, "{}") : FakeApi.Json(HttpStatusCode.Created, "{\"id\":1}"),
            delay: (d, _) => { delays.Add(d); return Task.CompletedTask; });

        result.Created.Should().Be(1);
        api.Posted.Should().HaveCount(3);
        delays.Should().HaveCount(2);
        delays[0].Should().BeGreaterThan(TimeSpan.FromMilliseconds(450));
        delays[1].Should().BeGreaterThan(TimeSpan.FromMilliseconds(900)).And.BeGreaterThan(delays[0]);
    }

    [Fact]
    public async Task Import_RetryAfterHeader_IsHonouredInsteadOfBackoff()
    {
        var delays = new List<TimeSpan>();
        var (result, _) = await Import("name\nAda\n",
            setup: a => a.OnPost = n =>
            {
                if (n > 1) return FakeApi.Json(HttpStatusCode.Created, "{\"id\":1}");
                var r = FakeApi.Json(HttpStatusCode.ServiceUnavailable, "{}");
                r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
                return r;
            },
            delay: (d, _) => { delays.Add(d); return Task.CompletedTask; });

        result.Created.Should().Be(1);
        delays.Should().ContainSingle().Which.Should().BeCloseTo(TimeSpan.FromSeconds(7), TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task Import_ThrottledForever_GivesUpAndReportsFailure()
    {
        var (result, api) = await Import("name\nAda\n", setup: a => a.OnPost = _ => FakeApi.Json((HttpStatusCode)429, "{}"));
        result.Failed.Should().Be(1);
        api.Posted.Should().HaveCount(DataImportRunner.MaxAttempts);
    }

    [Fact]
    public async Task Import_Concurrency_NeverExceedsTheConfiguredCap()
    {
        var rows = string.Join("\n", Enumerable.Range(0, 30).Select(i => $"{{\"name\":\"n{i}\"}}")) + "\n";
        var (result, api) = await Import(rows, "in.jsonl", a => a.PostLatency = TimeSpan.FromMilliseconds(20), o => o with { Concurrency = 3 });

        result.Created.Should().Be(30);
        api.MaxInFlight.Should().BeInRange(2, 3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public async Task Import_ConcurrencyOutOfRange_IsRefused(int n)
    {
        var act = () => Import("name\nAda\n", tweak: o => o with { Concurrency = n });
        await act.Should().ThrowAsync<CliException>();
    }

    // ── Dry run and confirmation ─────────────────────────────────────────────

    [Fact]
    public async Task Import_DryRun_SendsNothingAndReportsBadRows()
    {
        var (result, api) = await Import("name,age\nAda,1\nBo,x\n", tweak: o => o with { DryRun = true, Yes = false });

        api.Posted.Should().BeEmpty();
        result.WouldCreate.Should().Be(1);
        result.Created.Should().Be(0);
        result.Skipped.Should().Be(1);
    }

    [Fact]
    public async Task Import_NonInteractiveWithoutYes_RefusesAndSendsNothing()
    {
        var api = new FakeApi();
        using var dir = new TempDir();
        var file = dir.File("in.csv", "name\nAda\n");

        var act = () => new DataImportRunner(api.Client(), NoDelay).RunAsync(new DataImportOptions("customers", file, Interactive: false));

        await act.Should().ThrowAsync<CliException>().WithMessage("*--yes*");
        api.Posted.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_InteractiveDecline_SendsNothing()
    {
        var api = new FakeApi();
        using var dir = new TempDir();
        string? prompt = null;
        var file = dir.File("in.csv", "name\nAda\nBo\n");

        var result = await new DataImportRunner(api.Client(), NoDelay, p => { prompt = p; return false; })
            .RunAsync(new DataImportOptions("customers", file, Interactive: true));

        result.Cancelled.Should().BeTrue();
        api.Posted.Should().BeEmpty();
        prompt.Should().Contain("customers").And.Contain("about 2");
    }

    [Fact]
    public async Task Import_JsonArrayEstimate_IsUnknown()
    {
        using var dir = new TempDir();
        string? prompt = null;
        var file = dir.File("in.json", "[{\"name\":\"Ada\"}]");

        await new DataImportRunner(new FakeApi().Client(), NoDelay, p => { prompt = p; return false; })
            .RunAsync(new DataImportOptions("customers", file, Interactive: true));

        prompt.Should().Contain("unknown");
    }

    [Fact]
    public async Task Import_AnyFailedOrSkippedRow_MeansProblemsSoExitCodeIsNonZero()
    {
        (await Import("name\nAda\n")).Result.HasProblems.Should().BeFalse();
        (await Import("name,age\nAda,x\n")).Result.HasProblems.Should().BeTrue();
        (await Import("name\nAda\n", setup: a => a.OnPost = _ => FakeApi.Json(HttpStatusCode.BadRequest, "{}"))).Result.HasProblems.Should().BeTrue();
    }

    // ── System, collection and id columns ────────────────────────────────────

    [Fact]
    public async Task Import_SystemColumns_AreNeverSentAndReportedOnce()
    {
        var (result, api) = await Import("id,created_at,updated_at,tenant_id,locked,name\n5,2024-01-01,2024-01-02,9,false,Ada\n6,2024-01-01,2024-01-02,9,false,Bo\n");

        result.Created.Should().Be(2);
        Body(api).Select(kv => kv.Key).Should().Equal("name");
        result.Notes.Should().ContainSingle(n => n.StartsWith("ignored system columns:"))
            .Which.Should().Contain("id").And.Contain("created_at").And.Contain("locked");
    }

    [Fact]
    public async Task Import_ExportedRowWithOnlySystemValues_IsSkippedNeverPosted()
    {
        var (result, api) = await Import("id,created_at,name,zip\n5,2024-01-01,,\n");

        api.Posted.Should().BeEmpty();
        result.Skipped.Should().Be(1);
        result.Problems.Single().Should().Contain("no values");
    }

    [Fact]
    public async Task Import_OneToManyAndSecretColumns_AreSkippedAndReportedOnce()
    {
        var (result, api) = await Import("name,orders,api_secret\nAda,\"[{\"\"id\"\":1}]\",s3cret\nBo,\"[{\"\"id\"\":2}]\",s4cret\n");

        result.Created.Should().Be(2);
        api.Posted.Should().OnlyContain(p => !p.Contains("orders") && !p.Contains("api_secret") && !p.Contains("s3cret"));
        result.Notes.Should().ContainSingle(n => n.Contains("orders") && n.Contains("api_secret"));
    }

    [Fact]
    public async Task Import_ManyToManyAndUserColumns_AreKeptButWarnOncePerColumn()
    {
        var (result, api) = await Import("name,tags,owner\nAda,\"[1,2]\",7\nBo,\"[3]\",8\n");

        Body(api)["owner"]!.GetValue<long>().Should().Be(7);
        result.Notes.Where(n => n.Contains("'tags'")).Should().ContainSingle();
        result.Notes.Where(n => n.Contains("'owner'")).Should().ContainSingle().Which.Should().Contain("same project");
    }

    [Fact]
    public async Task Import_UserFieldThatIsNotAnId_IsRejected()
    {
        var (result, api) = await Import("name,owner\nAda,bob\n");
        api.Posted.Should().BeEmpty();
        result.Skipped.Should().Be(1);
    }

    // ── Types ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_JsonNumberIntoDecimalField_IsSentAsExactString()
    {
        var (_, api) = await Import("{\"name\":\"a\",\"price\":1.10}\n", "in.jsonl");
        Body(api)["price"]!.GetValue<string>().Should().Be("1.10");
    }

    [Fact]
    public async Task Import_EmptyBooleanCell_IsSentAsExplicitNullNotFalse()
    {
        var (_, api) = await Import("name,active\nAda,\n");
        Body(api).ContainsKey("active").Should().BeTrue();
        Body(api)["active"].Should().BeNull();
    }

    [Fact]
    public async Task Import_JsonbPlainString_IsPassedThroughLikeDataCreateDoes()
    {
        var (_, api) = await Import("name,meta\nAda,hello\n");
        Body(api)["meta"]!.GetValue<string>().Should().Be("hello");
    }

    [Fact]
    public async Task Import_TimestampWithOffset_IsConvertedToUtc()
    {
        var (_, api) = await Import("name,seen\nAda,2025-03-31T09:30:00+02:00\n");
        Body(api)["seen"]!.GetValue<string>().Should().Be("2025-03-31T07:30:00Z");
    }

    [Fact]
    public async Task Import_IntegerBeyondInt32_IsRejectedButBigintAccepts()
    {
        var (result, api) = await Import("name,age,big\nAda,3000000000,\nBo,,3000000000\n");
        result.Skipped.Should().Be(1);
        api.Posted.Should().ContainSingle().Which.Should().Contain("3000000000");
        Body(api)["big"]!.GetValue<long>().Should().Be(3000000000);
    }

    [Fact]
    public async Task Import_CsvWithByteOrderMark_DoesNotPolluteTheFirstHeader()
    {
        var (result, api) = await Import("\uFEFFname,age\nAda,3\n");
        result.Created.Should().Be(1);
        Body(api)["name"]!.GetValue<string>().Should().Be("Ada");
    }

    [Fact]
    public async Task Import_JsonArrayWithByteOrderMarkAndUnknownExtension_IsSniffedAsJson()
    {
        var (result, _) = await Import("\uFEFF[{\"name\":\"Ada\"}]", "data.dat");
        result.Created.Should().Be(1);
    }

    // ── Errors file ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_CsvRowWithExtraCells_KeepsTheExtrasInTheErrorsFile()
    {
        using var dir = new TempDir();
        var errors = dir.File("bad.csv");
        var file = dir.File("in.csv", "name,age\nAda,1,extra1,extra2\n");

        await new DataImportRunner(new FakeApi().Client(), NoDelay).RunAsync(new DataImportOptions("customers", file, Yes: true, ErrorsPath: errors));

        File.ReadAllLines(errors)[1].Should().EndWith("extra1,extra2");
    }

    [Fact]
    public async Task Import_ErrorsPathSameAsInput_IsRefusedBeforeAnythingIsRead()
    {
        using var dir = new TempDir();
        var file = dir.File("in.csv", "name\nAda\n");
        var api = new FakeApi();

        var act = () => new DataImportRunner(api.Client(), NoDelay).RunAsync(new DataImportOptions("customers", file, Yes: true, ErrorsPath: file));

        await act.Should().ThrowAsync<CliException>();
        api.Posted.Should().BeEmpty();
        File.ReadAllText(file).Should().Be("name\nAda\n");
    }

    [Fact]
    public async Task Import_CleanRun_RemovesStaleErrorsFileFromEarlierRun()
    {
        using var dir = new TempDir();
        var errors = dir.File("bad.csv", "old");
        var file = dir.File("in.csv", "name\nAda\n");

        var result = await new DataImportRunner(new FakeApi().Client(), NoDelay).RunAsync(new DataImportOptions("customers", file, Yes: true, ErrorsPath: errors));

        File.Exists(errors).Should().BeFalse();
        result.Notes.Should().Contain(n => n.Contains("left over"));
    }

    // ── Uncertain outcomes and rate limits ───────────────────────────────────

    [Fact]
    public async Task Import_ConnectionFailureBeforeSending_IsRetried()
    {
        var (result, api) = await Import("name\nAda\n",
            setup: a => a.OnPostThrow = n => n < 2 ? new HttpRequestException(HttpRequestError.ConnectionError, "refused") : null);

        result.Created.Should().Be(1);
        api.Posted.Should().HaveCount(2);
    }

    [Fact]
    public async Task Import_ResponseCutOffAfterSending_IsNotRetriedAndMarkedUncertain()
    {
        using var dir = new TempDir();
        var errors = dir.File("bad.csv");
        var api = new FakeApi { OnPostThrow = _ => new HttpRequestException(HttpRequestError.ResponseEnded, "cut off") };
        var file = dir.File("in.csv", "name\nAda\n");

        var result = await new DataImportRunner(api.Client(), NoDelay).RunAsync(new DataImportOptions("customers", file, Yes: true, ErrorsPath: errors));

        api.Posted.Should().ContainSingle();
        result.Failed.Should().Be(1);
        result.Problems.Single().Should().Contain("UNCERTAIN:").And.Contain("check before re-importing");
        File.ReadAllLines(errors)[1].Should().Contain("UNCERTAIN:");
    }

    [Fact]
    public async Task Import_Timeout_IsNotRetriedAndMarkedUncertain()
    {
        var (result, api) = await Import("name\nAda\n", setup: a => a.OnPostThrow = _ => new TaskCanceledException("timeout"));
        api.Posted.Should().ContainSingle();
        result.Problems.Single().Should().Contain("UNCERTAIN:");
    }

    [Fact]
    public async Task Import_SuccessResponseWithoutId_IsNotCountedAsCreated()
    {
        var (result, _) = await Import("name\nAda\n", setup: a => a.OnPost = _ => FakeApi.Json(HttpStatusCode.Created, "{}"));
        result.Created.Should().Be(0);
        result.Failed.Should().Be(1);
    }

    [Fact]
    public async Task Import_Http200WithEmptyBody_IsNotCountedAsCreated()
    {
        var (result, _) = await Import("name\nAda\n", setup: a => a.OnPost = _ => FakeApi.Json(HttpStatusCode.OK, ""));
        result.Created.Should().Be(0);
        result.Problems.Single().Should().Contain("UNCERTAIN:");
    }

    [Fact]
    public async Task Import_Http201WithEmptyBody_IsCountedAsCreated()
    {
        var (result, _) = await Import("name\nAda\n", setup: a => a.OnPost = _ => FakeApi.Json(HttpStatusCode.Created, ""));
        result.Created.Should().Be(1);
    }

    [Fact]
    public async Task Import_Http429WithRetryAfterInBody_IsHonoured()
    {
        var delays = new List<TimeSpan>();
        var (result, _) = await Import("name\nAda\n",
            setup: a => a.OnPost = n => n == 1 ? FakeApi.Json((HttpStatusCode)429, "{\"retry_after\":30}") : FakeApi.Json(HttpStatusCode.Created, "{\"id\":1}"),
            delay: (d, _) => { delays.Add(d); return Task.CompletedTask; });

        result.Created.Should().Be(1);
        delays.Should().ContainSingle().Which.Should().BeCloseTo(TimeSpan.FromSeconds(30), TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task Import_Http429WithRateLimitResetHeader_IsHonoured()
    {
        var delays = new List<TimeSpan>();
        var (_, _) = await Import("name\nAda\n",
            setup: a => a.OnPost = n =>
            {
                if (n > 1) return FakeApi.Json(HttpStatusCode.Created, "{\"id\":1}");
                var r = FakeApi.Json((HttpStatusCode)429, "{}");
                r.Headers.Add("X-RateLimit-Reset", "12");
                return r;
            },
            delay: (d, _) => { delays.Add(d); return Task.CompletedTask; });

        delays.Should().ContainSingle().Which.Should().BeCloseTo(TimeSpan.FromSeconds(12), TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public void RateGate_PauseByOneWorker_DelaysEveryoneAndNeverShortens()
    {
        var gate = new RateGate();
        gate.Pause(TimeSpan.FromSeconds(30));
        gate.Pause(TimeSpan.FromSeconds(5));

        gate.Remaining(DateTime.UtcNow).Should().BeGreaterThan(TimeSpan.FromSeconds(25));
        gate.Remaining(DateTime.UtcNow.AddSeconds(31)).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Import_ThrottledWithoutRetryAfter_BacksOffOverAMinuteBeforeGivingUp()
    {
        var delays = new List<TimeSpan>();
        var (result, _) = await Import("name\nAda\n",
            setup: a => a.OnPost = _ => FakeApi.Json((HttpStatusCode)429, "{}"),
            delay: (d, _) => { delays.Add(d); return Task.CompletedTask; });

        result.Failed.Should().Be(1);
        delays.Sum(d => d.TotalSeconds).Should().BeGreaterThan(60);
    }

    [Fact]
    public async Task Import_Interrupted_StopsReadingAndReportsPartialResultAsProblem()
    {
        using var dir = new TempDir();
        var file = dir.File("in.csv", "name\nAda\nBo\n");
        var api = new FakeApi();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await new DataImportRunner(api.Client(), NoDelay).RunAsync(new DataImportOptions("customers", file, Yes: true), cts.Token);

        result.Interrupted.Should().BeTrue();
        result.HasProblems.Should().BeTrue();
        api.Posted.Should().BeEmpty();
    }
}
