using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AnythinkCli.Client;
using AnythinkCli.Commands;
using AnythinkCli.Config;
using AnythinkCli.DataTransfer;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class DataCsvTests
{
    private static List<List<string>> Read(string text) => Csv.ReadRecords(new StringReader(text)).ToList();

    [Fact]
    public void Csv_QuotingRoundTrip_KeepsCommasQuotesAndMultiLineCells()
    {
        string[] cells = ["plain", "a,b", "say \"hi\"", "line1\nline2", "cr\r\nlf", "", " padded "];
        var sw = new StringWriter();
        Csv.WriteRow(sw, cells);
        Csv.WriteRow(sw, cells);

        var records = Read(sw.ToString());

        records.Should().HaveCount(2);
        records[0].Should().Equal(cells);
        records[1].Should().Equal(cells);
    }

    [Fact]
    public void Csv_CellsNeedingQuotes_AreQuotedAndOthersAreNot()
    {
        Csv.Escape("a,b").Should().Be("\"a,b\"");
        Csv.Escape("q\"q").Should().Be("\"q\"\"q\"");
        Csv.Escape("x\ny").Should().Be("\"x\ny\"");
        Csv.Escape("x\ry").Should().Be("\"x\ry\"");
        Csv.Escape("plain").Should().Be("plain");
    }

    [Theory]
    [InlineData("=1+1")]
    [InlineData("+1")]
    [InlineData("-2")]
    [InlineData("@cmd")]
    [InlineData("\tx")]
    [InlineData("\rx")]
    public void Csv_FormulaGuard_PrefixesTextThatCouldRunAsAFormula(string text)
    {
        Csv.ToCell(JsonValue.Create(text), formulaGuard: true).Should().Be("'" + text);
        Csv.ToCell(JsonValue.Create(text), formulaGuard: false).Should().Be(text);
    }

    [Fact]
    public void Csv_FormulaGuard_LeavesNegativeNumbersAndOrdinaryTextAlone()
    {
        Csv.ToCell(JsonValue.Create(-5), true).Should().Be("-5");
        Csv.ToCell(JsonValue.Create("hello"), true).Should().Be("hello");
        Csv.ToCell(null, true).Should().BeNull();
    }

    [Fact]
    public void Csv_StructuredValues_AreWrittenAsCompactJsonText()
    {
        Csv.ToCell(JsonNode.Parse("{ \"a\": [1, 2] }"), true).Should().Be("{\"a\":[1,2]}");
        Csv.ToCell(JsonValue.Create(true), true).Should().Be("true");
    }

    [Fact]
    public void Csv_RecordLargerThanTheCap_IsRejectedInsteadOfBufferingTheWholeFile()
    {
        var act = () => Csv.ReadRecords(new StringReader("\"" + new string('a', 100) + "\nmore"), maxRecordChars: 50).ToList();
        act.Should().Throw<FormatException>().WithMessage("*larger than*");
    }

    [Fact]
    public void Csv_CrLfAndLfRecordEndings_AreBothAccepted()
    {
        Read("a,b\r\n1,2\n3,4").Should().HaveCount(3);
    }
}

public class DataExportTests
{
    private static string Page(int from, int count, bool next) =>
        $"{{\"items\":[{string.Join(",", Enumerable.Range(from, count).Select(i => $"{{\"name\":\"n{i}\",\"age\":{i}}}"))}],\"has_next_page\":{(next ? "true" : "false")}}}";

    private static FakeApi Paged(int pages, int perPage = 2)
    {
        var api = new FakeApi();
        api.OnGetItems = uri =>
        {
            var page = int.Parse(System.Web.HttpUtility.ParseQueryString(uri.Query)["page"]!);
            return FakeApi.Json(HttpStatusCode.OK, Page((page - 1) * perPage, perPage, page < pages));
        };
        return api;
    }

    private static DataExportOptions Opts(string path, DataFormat format, Func<DataExportOptions, DataExportOptions>? tweak = null)
        => tweak?.Invoke(new DataExportOptions("customers", path, format)) ?? new DataExportOptions("customers", path, format);

    [Fact]
    public async Task Export_AllPages_AreFetchedWithPageAndPageSize()
    {
        using var dir = new TempDir();
        var api = Paged(pages: 3);

        var rows = await DataExportRunner.RunAsync(api.Client(), Opts(dir.File("o.jsonl"), DataFormat.Jsonl, o => o with { PageSize = 2 }));

        rows.Should().Be(6);
        var itemCalls = api.Gets.Where(g => g.Contains("/items")).ToList();
        itemCalls.Should().HaveCount(3);
        itemCalls[2].Should().Contain("page=3").And.Contain("pageSize=2");
        File.ReadAllLines(dir.File("o.jsonl")).Should().HaveCount(6);
    }

    [Fact]
    public async Task Export_Filter_IsPassedAsPerFieldQueryParams()
    {
        using var dir = new TempDir();
        var api = Paged(1);

        await DataExportRunner.RunAsync(api.Client(), Opts(dir.File("o.jsonl"), DataFormat.Jsonl, o => o with { Filter = "status=draft&age=GTE:5" }));

        api.Gets.Single(g => g.Contains("/items")).Should().Contain("&status=draft").And.Contain("&age=GTE%3A5");
    }

    [Fact]
    public async Task Export_JsonArray_IsValidJsonWithAllRows()
    {
        using var dir = new TempDir();

        await DataExportRunner.RunAsync(Paged(2).Client(), Opts(dir.File("o.json"), DataFormat.Json));

        var array = JsonNode.Parse(File.ReadAllText(dir.File("o.json")))!.AsArray();
        array.Should().HaveCount(4);
        array[3]!["name"]!.GetValue<string>().Should().Be("n3");
    }

    [Fact]
    public async Task Export_JsonWithNoRows_IsAnEmptyArray()
    {
        using var dir = new TempDir();
        await DataExportRunner.RunAsync(new FakeApi().Client(), Opts(dir.File("o.json"), DataFormat.Json));
        JsonNode.Parse(File.ReadAllText(dir.File("o.json")))!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Export_Csv_HeaderComesFromEntityFieldsAndValuesAreEscaped()
    {
        using var dir = new TempDir();
        var api = new FakeApi
        {
            OnGetItems = _ => FakeApi.Json(HttpStatusCode.OK,
                """{"items":[{"name":"=HYPERLINK(\"x\")","zip":"a,b","age":3,"meta":{"k":[1]},"price":-1.5}],"has_next_page":false}""")
        };

        await DataExportRunner.RunAsync(api.Client(), Opts(dir.File("o.csv"), DataFormat.Csv));

        var records = Csv.ReadRecords(new StringReader(File.ReadAllText(dir.File("o.csv")))).ToList();
        records[0].Should().Equal("id", "created_at", "name", "zip", "age", "price", "active", "meta", "born", "status", "seen", "big", "tags", "owner", "api_secret");
        string Cell(string column) => records[1][records[0].IndexOf(column)];
        Cell("name").Should().Be("'=HYPERLINK(\"x\")");
        Cell("zip").Should().Be("a,b");
        Cell("price").Should().Be("-1.5");
        Cell("meta").Should().Be("{\"k\":[1]}");
    }

    [Fact]
    public async Task Export_OneToManyColumns_AreLeftOutUnlessNamedInFields()
    {
        using var dir = new TempDir();
        var api = new FakeApi
        {
            OnGetItems = _ => FakeApi.Json(HttpStatusCode.OK, """{"items":[{"name":"Ada","orders":[{"id":1,"__name":"o"}]}],"has_next_page":false}""")
        };

        await DataExportRunner.RunAsync(api.Client(), Opts(dir.File("a.csv"), DataFormat.Csv));
        await DataExportRunner.RunAsync(api.Client(), Opts(dir.File("a.jsonl"), DataFormat.Jsonl));
        await DataExportRunner.RunAsync(api.Client(), Opts(dir.File("b.csv"), DataFormat.Csv, o => o with { Fields = ["name", "orders"] }));

        File.ReadAllLines(dir.File("a.csv"))[0].Should().NotContain("orders");
        File.ReadAllText(dir.File("a.jsonl")).Should().NotContain("orders");
        File.ReadAllLines(dir.File("b.csv"))[0].Should().Be("name,orders");
    }

    [Fact]
    public async Task Export_Cancelled_LeavesExistingFileUntouchedAndNoTempBehind()
    {
        using var dir = new TempDir();
        var path = dir.File("o.jsonl", "precious");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => DataExportRunner.RunAsync(Paged(2).Client(), Opts(path, DataFormat.Jsonl, o => o with { Force = true }), ct: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(path).Should().Be("precious");
        Directory.GetFileSystemEntries(dir.Path).Should().ContainSingle();
    }

    [Fact]
    public async Task Export_FieldsOption_SelectsAndOrdersColumns()
    {
        using var dir = new TempDir();

        await DataExportRunner.RunAsync(Paged(1).Client(), Opts(dir.File("o.csv"), DataFormat.Csv, o => o with { Fields = ["age", "name"] }));

        File.ReadAllLines(dir.File("o.csv"))[0].Should().Be("age,name");
    }

    [Fact]
    public async Task Export_ExistingFileWithoutForce_IsRefusedAndUntouched()
    {
        using var dir = new TempDir();
        var path = dir.File("o.csv", "precious");
        var api = Paged(1);

        var act = () => DataExportRunner.RunAsync(api.Client(), Opts(path, DataFormat.Csv));

        await act.Should().ThrowAsync<CliException>().WithMessage("*--force*");
        File.ReadAllText(path).Should().Be("precious");
        api.Gets.Should().BeEmpty();
    }

    [Fact]
    public async Task Export_FailurePartWay_LeavesExistingFileUntouchedAndNoTempBehind()
    {
        using var dir = new TempDir();
        var path = dir.File("o.jsonl", "precious");
        var api = Paged(3);
        var inner = api.OnGetItems!;
        api.OnGetItems = uri => uri.Query.Contains("page=2") ? FakeApi.Json(HttpStatusCode.InternalServerError, "boom") : inner(uri);

        var act = () => DataExportRunner.RunAsync(api.Client(), Opts(path, DataFormat.Jsonl, o => o with { Force = true }));

        await act.Should().ThrowAsync<AnythinkException>();
        File.ReadAllText(path).Should().Be("precious");
        Directory.GetFileSystemEntries(dir.Path).Should().ContainSingle();
    }

    [Fact]
    public async Task Export_Force_ReplacesExistingFileOnSuccess()
    {
        using var dir = new TempDir();
        var path = dir.File("o.jsonl", "old");

        await DataExportRunner.RunAsync(Paged(1).Client(), Opts(path, DataFormat.Jsonl, o => o with { Force = true }));

        File.ReadAllText(path).Should().Contain("n0");
        Directory.GetFileSystemEntries(dir.Path).Should().ContainSingle();
    }

    [Fact]
    public async Task Export_DashWritesToStdout()
    {
        var buffer = new MemoryStream();

        var rows = await DataExportRunner.RunAsync(Paged(1).Client(), Opts("-", DataFormat.Jsonl), buffer);

        rows.Should().Be(2);
        Encoding.UTF8.GetString(buffer.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task Export_PageSizeOutOfRange_IsRefused(int size)
    {
        var act = () => DataExportRunner.RunAsync(new FakeApi().Client(), Opts("-", DataFormat.Jsonl, o => o with { PageSize = size }));
        await act.Should().ThrowAsync<CliException>();
    }

    [Fact]
    public void Export_FormatFromExtension_CoversAllSupportedSuffixes()
    {
        RowReader.FromExtension("a.csv").Should().Be(DataFormat.Csv);
        RowReader.FromExtension("a.json").Should().Be(DataFormat.Json);
        RowReader.FromExtension("a.jsonl").Should().Be(DataFormat.Jsonl);
        RowReader.FromExtension("a.NDJSON").Should().Be(DataFormat.Jsonl);
        RowReader.FromExtension("a.txt").Should().BeNull();
    }

    // ── Exit codes ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_ThenExport_RoundTripsAwkwardCsvCellsLosslessly()
    {
        using var dir = new TempDir();
        var api = new FakeApi();
        var source = dir.File("in.csv", "name,zip\r\n\"Ada, \"\"A\"\"\nLovelace\",00123\r\n");

        await new DataImportRunner(api.Client(), (_, _) => Task.CompletedTask).RunAsync(new DataImportOptions("customers", source, Yes: true));
        var posted = JsonNode.Parse(api.Posted.Single())!.AsObject();
        posted["name"]!.GetValue<string>().Should().Be("Ada, \"A\"\nLovelace");
        posted["zip"]!.GetValue<string>().Should().Be("00123");

        var echo = new FakeApi { OnGetItems = _ => FakeApi.Json(HttpStatusCode.OK, $"{{\"items\":[{posted.ToJsonString()}],\"has_next_page\":false}}") };
        await DataExportRunner.RunAsync(echo.Client(), Opts(dir.File("out.csv"), DataFormat.Csv, o => o with { Fields = ["name", "zip"] }));
        var again = Csv.ReadRecords(new StringReader(File.ReadAllText(dir.File("out.csv")))).Last();
        again.Should().Equal("Ada, \"A\"\nLovelace", "00123");
    }
}

[Collection("SequentialConfig")]
public class DataTransferExitCodeTests : IDisposable
{
    private readonly string _config = Directory.CreateTempSubdirectory("anythink-cfg-").FullName;
    private readonly TempDir _dir = new();

    public DataTransferExitCodeTests() => ConfigService.ConfigDirOverride = _config;

    public void Dispose()
    {
        ConfigService.ConfigDirOverride = null;
        Directory.Delete(_config, true);
        _dir.Dispose();
    }

    private Task<int> RunImport(string csv, FakeApi api) =>
        new DataImportCommand { ClientFactory = api.Client }.ExecuteAsync(null!,
            new DataImportSettings { Entity = "customers", File = _dir.File("in.csv", csv), Yes = true });

    [Fact]
    public async Task ImportCommand_CleanFile_ExitsZero() => (await RunImport("name\nAda\n", new FakeApi())).Should().Be(0);

    [Fact]
    public async Task ImportCommand_SkippedRow_ExitsNonZero() => (await RunImport("name,age\nAda,1\nBo,x\n", new FakeApi())).Should().Be(1);

    [Fact]
    public async Task ImportCommand_ServerRejectedRow_ExitsNonZero() =>
        (await RunImport("name\nAda\n", new FakeApi { OnPost = _ => FakeApi.Json(HttpStatusCode.BadRequest, "{}") })).Should().Be(1);

    [Fact]
    public async Task ImportCommand_NonInteractiveWithoutYes_ExitsNonZeroAndSendsNothing()
    {
        var api = new FakeApi();
        var code = await new DataImportCommand { ClientFactory = api.Client }.ExecuteAsync(null!,
            new DataImportSettings { Entity = "customers", File = _dir.File("in.csv", "name\nAda\n") });
        code.Should().Be(1);
        api.Posted.Should().BeEmpty();
    }

    [Fact]
    public async Task ExportCommand_NoCredentials_ExitsNonZero()
    {
        var settings = new DataExportSettings { Entity = "customers", File = _dir.File("o.csv"), Format = "csv" };
        (await new DataExportCommand().ExecuteAsync(null!, settings)).Should().Be(1);
    }

    [Fact]
    public async Task ImportCommand_NoCredentials_ExitsNonZero()
    {
        var settings = new DataImportSettings { Entity = "customers", File = _dir.File("in.csv", "name\nAda\n"), Yes = true };
        (await new DataImportCommand().ExecuteAsync(null!, settings)).Should().Be(1);
    }

    [Fact]
    public async Task ExportCommand_UnknownFormat_ExitsNonZero()
    {
        var settings = new DataExportSettings { Entity = "customers", File = "x.csv", Format = "xml" };
        (await new DataExportCommand().ExecuteAsync(null!, settings)).Should().Be(1);
    }

    [Fact]
    public async Task ExportCommand_ToStdout_SendsErrorsToStderrNotTheDataStream()
    {
        var (stdout, stderr) = (Console.Out, Console.Error);
        var (outText, errText) = (new StringWriter(), new StringWriter());
        Console.SetOut(outText);
        Console.SetError(errText);
        try
        {
            var command = new DataExportCommand { ClientFactory = () => throw new CliException("boom [red]x[/]") };
            var code = await command.ExecuteAsync(null!, new DataExportSettings { Entity = "customers", File = "-", Format = "jsonl" });
            code.Should().Be(1);
        }
        finally { Console.SetOut(stdout); Console.SetError(stderr); }

        outText.ToString().Should().BeEmpty();
        errText.ToString().Should().Contain("boom x");
    }
}
