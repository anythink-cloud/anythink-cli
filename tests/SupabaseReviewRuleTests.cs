using System.Net;
using Anythink.ProjectImporters;
using Anythink.ProjectImporters.Supabase;
using AnythinkCli.Client;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class SupabaseReviewRuleTests
{
    private static readonly string[] Schemas = ["public"];

    private static Task<ImportSchema> Schema(FakeSupabaseSource source) =>
        SupabaseImporterTests.Importer(source).FetchSchemaAsync(false, false, false);

    // ── Rule: nothing outlives the transaction on a pooled connection ──

    [Fact]
    public async Task SupabaseImport_NeverChangesSessionSettings_SoPooledConnectionsAreUntouched()
    {
        var seen = new List<string>();
        await ReadOnlySql.PrepareTransactionAsync(statement => { seen.Add(statement); return Task.FromResult<object?>("on"); });

        seen.Should().Equal(
            "SET TRANSACTION READ ONLY",
            "SET LOCAL row_security = off",
            "SET LOCAL TimeZone = 'UTC'",
            "SET LOCAL DateStyle = 'ISO, YMD'",
            "SHOW transaction_read_only");
        seen.Should().NotContain(s => s.Contains("SESSION", StringComparison.OrdinalIgnoreCase));
        seen.Where(s => s.StartsWith("SET")).Should().OnlyContain(s => s.StartsWith("SET LOCAL") || s == "SET TRANSACTION READ ONLY");
    }

    [Fact]
    public async Task SupabaseImport_ASessionThatIsNotReadOnly_StopsBeforeAnyRead()
    {
        var act = () => ReadOnlySql.PrepareTransactionAsync(_ => Task.FromResult<object?>("off"));

        await act.Should().ThrowAsync<ImportException>().WithMessage("*not read-only*");
    }

    // ── Rule: a role that cannot read everything fails loudly instead of returning filtered rows ──

    [Fact]
    public void SupabaseImport_RowLevelSecurity_IsSwitchedOffSoUnprivilegedRolesErrorRatherThanGetFilteredRows()
    {
        ReadOnlySql.TransactionSetup.Should().Contain("SET LOCAL row_security = off");
        ReadOnlySql.TransactionSetup[0].Should().Be("SET TRANSACTION READ ONLY");
    }

    [Fact]
    public void SupabaseImport_TheCatalogue_ComesFromPgClassNotInformationSchema()
    {
        PostgresSupabaseSource.ColumnsSql.Should().Contain("pg_class").And.Contain("pg_attribute").And.Contain("pg_namespace")
            .And.NotContain("information_schema");
    }

    [Fact]
    public void SupabaseImport_TheDbHelp_SaysTheRoleMustReadEveryTableBypassingRowLevelSecurity()
    {
        var help = typeof(Commands.ImportSupabaseSettings).GetProperty("Db")!
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).Cast<System.ComponentModel.DescriptionAttribute>().Single().Description;

        help.Should().Contain("every table").And.Contain("BYPASSRLS").And.Contain("read-only");
    }

    // ── Rule: the keyset cursor round-trips exactly, whatever the display form ──

    [Theory]
    [InlineData("timestamptz")]
    [InlineData("bytea")]
    [InlineData("_text")]
    public async Task SupabaseImport_KeyCursor_IsThePlainTextOfTheTypedKeyNotTheDisplayForm(string udt)
    {
        var runner = new FakeSqlRunner();
        var column = SupabaseImporterTests.Col("t", "k", udt, udt.StartsWith('_') ? "ARRAY" : null);
        var table = new SupabaseTableRef("public", "t", [column], [new("k", column.UdtSchema, udt)]);

        await new PostgresSupabaseSource(runner, "db").ReadRowsAsync(table, ["\\x01"], 10);

        var (sql, parameters) = runner.Calls.Single();
        sql.Should().Contain(", \"k\"::text FROM ONLY").And.Contain($"$1::\"pg_catalog\".\"{udt}\"");
        parameters[0].Should().Be("\\x01");
    }

    [Fact]
    public async Task SupabaseImport_NextPage_ContinuesFromTheCursorCellsNotTheDisplayCells()
    {
        var source = new FakeSupabaseSource { CursorOf = display => "raw:" + display };
        source.Columns.Add(SupabaseImporterTests.Col("t", "k", "timestamptz"));
        source.Constraints.Add(SupabaseImporterTests.Pk("t", "k"));
        source.Rows["public.t"] = [["2024-01-01 00:00:00"], ["2024-01-02 00:00:00"]];
        var importer = SupabaseImporterTests.Importer(source);
        await importer.FetchSchemaAsync(false, false, false);

        await importer.FetchRecordsAsync("t", 1, 1);
        await importer.FetchRecordsAsync("t", 2, 1);

        source.Cursors[1].Should().Equal("raw:2024-01-01 00:00:00");
    }

    // ── Rule: partitions, foreign tables and extension tables are handled and visible ──

    [Fact]
    public void SupabaseImport_PartitionChildren_AreNotTablesOfTheirOwn_AndRowsAreReadFromOnlyThatTable()
    {
        PostgresSupabaseSource.ColumnsSql.Should().Contain("NOT c.relispartition");
        var column = SupabaseImporterTests.Col("t", "id", "int4");
        var plain = new SupabaseTableRef("public", "t", [column], [new("id", "pg_catalog", "int4")]);
        var partitioned = plain with { Partitioned = true };

        PostgresSupabaseSource.RowsSql(plain, false).Should().Contain("FROM ONLY ");
        PostgresSupabaseSource.RowsSql(partitioned, false).Should().NotContain("ONLY");
    }

    [Fact]
    public async Task SupabaseImport_PartitionedParents_AreReadThroughTheirPartitions()
    {
        var source = SupabaseImporterTests.Todos();
        source.Columns.Clear();
        source.Columns.Add(new SupabaseColumn("public", "todos", "id", "uuid", "pg_catalog", "uuid", null, true, 'p'));
        source.Rows["public.todos"] = [["11111111-1111-1111-1111-111111111111"]];
        var importer = SupabaseImporterTests.Importer(source);
        await importer.FetchSchemaAsync(false, false, false);

        await importer.FetchRecordsAsync("todos", 1, 10);

        source.LastTable!.Partitioned.Should().BeTrue();
    }

    [Fact]
    public async Task SupabaseImport_ForeignAndExtensionTables_AreListedAsNotBroughtAcross()
    {
        var source = SupabaseImporterTests.Todos();
        source.Skipped.AddRange([new("public", "remote_orders", 'f'), new("public", "spatial_ref_sys", 'r')]);

        var schema = await Schema(source);

        schema.Warnings.Should().Contain(w => w.StartsWith("1 foreign table(s)") && w.Contains("public.remote_orders"));
        schema.Warnings.Should().Contain(w => w.StartsWith("1 table(s) owned by extensions") && w.Contains("public.spatial_ref_sys"));
    }

    // ── Rule: key columns named id are handled without noise and without hiding a foreign key ──

    [Fact]
    public async Task SupabaseImport_PrimaryKeyIdThatIsAlsoAForeignKey_IsReported()
    {
        var source = SupabaseImporterTests.Todos();
        source.Columns.AddRange([SupabaseImporterTests.Col("profiles", "id", "uuid")]);
        source.Constraints.AddRange([SupabaseImporterTests.Pk("profiles", "id"),
            new('f', "public", "profiles", "profiles_id_fkey", ["id"], "auth", "users", ["id"])]);

        var schema = await Schema(source);

        schema.Warnings.Should().Contain(w => w.StartsWith("public.profiles.id:") && w.Contains("foreign key to auth.users") && w.Contains("supabase_id"));
    }

    [Fact]
    public async Task SupabaseImport_CompositeKeyColumnNamedId_IsKeptWithoutAClashWarning()
    {
        var source = new FakeSupabaseSource();
        source.Columns.AddRange([SupabaseImporterTests.Col("m", "id", "int4"), SupabaseImporterTests.Col("m", "team", "int4")]);
        source.Constraints.Add(SupabaseImporterTests.Pk("m", "id", "team"));

        var schema = await Schema(source);

        schema.Collections.Single().Fields.Select(f => f.Name).Should().Contain(["supabase_id", "supabase_id_column", "team"]);
        schema.Warnings.Should().NotContain(w => w.Contains("cannot be a field of its own"));
    }

    // ── Rule: cron counts say whose jobs they are; unsafe object names are not brought across ──

    [Fact]
    public async Task SupabaseImport_CronCount_SaysNonSuperusersSeeOnlyTheirOwnJobs()
    {
        var source = SupabaseImporterTests.Todos();
        source.Extensions.Add("pg_cron");
        source.CronJobs = 2;

        var schema = await Schema(source);

        schema.Warnings.Should().Contain(w => w.StartsWith("2 cron job(s)") && w.Contains("not a superuser sees only its own"));
    }

    [Fact]
    public async Task SupabaseImport_ObjectNamesWithDotDotSegments_AreNotBroughtAcross()
    {
        var source = SupabaseImporterTests.Todos();
        source.Objects.AddRange([new("pics", "../../etc/passwd"), new("pics", "a/../b.png"), new("pics", "a..b/c.png"), new("pics", "ok.png")]);

        var schema = await SupabaseImporterTests.Importer(source).FetchSchemaAsync(false, true, false);

        schema.Files.Select(f => f.SourceId).Should().BeEquivalentTo("pics/a..b/c.png", "pics/ok.png");
        schema.Warnings.Should().Contain(w => w.StartsWith("2 object(s) not brought across") && w.Contains("'..'"));
    }

    // ── Rule: the guard is defence in depth against more than writes ──

    [Theory]
    [InlineData("SELECT pg_notify('c', 'p')")]
    [InlineData("SELECT query_to_xml('delete from t', true, true, '')")]
    [InlineData("SELECT pg_reload_conf()")]
    [InlineData("SELECT pg_sleep(100)")]
    [InlineData("SELECT $$ x $$; DROP TABLE t")]
    [InlineData("SELECT $q$ unterminated")]
    [InlineData("SELECT $$ unterminated")]
    public void SupabaseImport_GuardRefusesSideEffectFunctionsAndUnbalancedDollarQuotes(string sql)
    {
        var act = () => ReadOnlySql.Guard(sql);

        act.Should().Throw<ImportException>();
    }

    [Theory]
    [InlineData("SELECT $$ drop table t $$ AS note")]
    [InlineData("SELECT $tag$ it's; fine $tag$")]
    [InlineData("SELECT a FROM t WHERE b > $1::int4 AND c < $2 LIMIT $3")]
    public void SupabaseImport_GuardAcceptsBalancedDollarQuotesAndParameters(string sql)
    {
        ReadOnlySql.Guard(sql).Should().Be(sql);
    }

    // ── Rule: Storage downloads send the key as apikey too and never follow a redirect ──

    [Fact]
    public void SupabaseImport_Downloads_SendTheKeyAsApikeyAndRefuseRedirects()
    {
        var importer = SupabaseImporterTests.Importer(SupabaseImporterTests.Todos());

        importer.SourceHeaders.Should().Equal(new Dictionary<string, string> { ["apikey"] = "service-key-value" });
        importer.SourceAuthToken.Should().Be("service-key-value");
        importer.AllowSourceRedirects.Should().BeFalse();
    }

    [Fact]
    public void Download_WithRedirectsOff_DoesNotAutoFollowThem()
    {
        AnythinkClient.DownloadHandler(followRedirects: false).AllowAutoRedirect.Should().BeFalse();
        AnythinkClient.DownloadHandler(followRedirects: true).AllowAutoRedirect.Should().BeTrue();
    }

    [Fact]
    public async Task Download_ARedirect_IsAnErrorNamingOnlyTheHost()
    {
        var handler = new RedirectHandler(new Uri("https://evil.example.net/steal?token=abc"));
        using var http = AnythinkClient.NewDownloader("bearer-token", new Dictionary<string, string> { ["apikey"] = "key" }, handler);

        var act = () => AnythinkClient.DownloadCappedAsync(http, "https://abcd.supabase.co/file", "f.bin", 10);

        var error = (await act.Should().ThrowAsync<AnythinkException>()).Which;
        error.Message.Should().Contain("evil.example.net").And.NotContain("steal").And.NotContain("abc");
        error.StatusCode.Should().Be(302);
        handler.Request!.Headers.GetValues("apikey").Should().Equal("key");
        handler.Request.Headers.Authorization!.Parameter.Should().Be("bearer-token");
    }

    private sealed class RedirectHandler(Uri location) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = location;
            return Task.FromResult(response);
        }
    }

    // ── Rule: ids are hashed by the shared helper ──

    [Fact]
    public void ImportNames_StableId_IsDeterministicAndBackingSupabaseRecordIds()
    {
        ImportNames.StableId("a").Should().Be(ImportNames.StableId("a")).And.NotBe(ImportNames.StableId("b"));
        SupabaseImporter.RecordId("todos", "k").Should().Be(ImportNames.StableId("todos\0k"));
    }
}
