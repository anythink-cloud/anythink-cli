using System.Reflection;
using Anythink.ProjectImporters;
using Anythink.ProjectImporters.Engine;
using Anythink.ProjectImporters.Supabase;
using FluentAssertions;

namespace AnythinkCli.Tests;

internal sealed class FakeSqlRunner(Func<string, object?[]?, IReadOnlyList<object?[]>>? reply = null) : ISqlRunner
{
    public List<(string Sql, object?[] Parameters)> Calls { get; } = [];

    public Task<IReadOnlyList<object?[]>> QueryAsync(string sql, params object?[] parameters)
    {
        Calls.Add((sql, parameters));
        return Task.FromResult(reply?.Invoke(sql, parameters) ?? []);
    }
}

public class SupabaseReadOnlyTests
{
    private static readonly string[] Schemas = ["public"];

    private static async Task CallEveryReader(PostgresSupabaseSource source)
    {
        await source.ListColumnsAsync(Schemas);
        await source.ListConstraintsAsync(Schemas);
        await source.ListSkippedTablesAsync(Schemas);
        await source.ListEnumsAsync();
        await source.ListPoliciesAsync(Schemas);
        await source.ListViewsAsync(Schemas);
        await source.ListRoutinesAsync(Schemas);
        await source.ListTriggersAsync(Schemas);
        await source.ListExtensionsAsync();
        await source.ListPublicationsAsync();
        await source.ListBucketsAsync();
        await source.ListObjectsAsync(null, null, 10);
        var table = new SupabaseTableRef("public", "t\"; DROP TABLE x; --", [SupabaseImporterTests.Col("t", "a\"b", "int4"), SupabaseImporterTests.Col("t", "update", "jsonb")],
            [new("a\"b", "pg_catalog", "int4")]);
        await source.ReadRowsAsync(table, null, 10);
        await source.ReadRowsAsync(table, ["1"], 10);
    }

    // ── Rule: the importer only ever sends plain SELECT statements ──

    [Fact]
    public async Task SupabaseImport_EverythingSentToThePlatform_IsAPlainSelect()
    {
        var runner = new FakeSqlRunner((sql, _) => sql.StartsWith("SELECT count(*)::bigint") ? [[1L]] : []);
        var source = new PostgresSupabaseSource(runner, "db");

        await CallEveryReader(source);
        await source.CountAuthUsersAsync();
        await source.CountCronJobsAsync();

        runner.Calls.Should().HaveCountGreaterThan(14);
        runner.Calls.Should().OnlyContain(c => c.Sql.TrimStart().StartsWith("SELECT ") || c.Sql.TrimStart().StartsWith("WITH "));
        runner.Calls.ForEach(c => ReadOnlySql.Guard(c.Sql));
    }

    [Fact]
    public void SupabaseImport_EveryCatalogueStatement_PassesTheReadOnlyGuard()
    {
        var statements = typeof(PostgresSupabaseSource).GetFields(BindingFlags.Static | BindingFlags.NonPublic)
            .Where(f => f.Name.EndsWith("Sql") && f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!).ToList();

        statements.Should().HaveCountGreaterThan(10);
        statements.ForEach(s => ReadOnlySql.Guard(s));
    }

    [Theory]
    [InlineData("INSERT INTO t VALUES (1)")]
    [InlineData("UPDATE t SET a = 1")]
    [InlineData("DELETE FROM t")]
    [InlineData("DROP TABLE t")]
    [InlineData("TRUNCATE t")]
    [InlineData("CREATE TABLE t (a int)")]
    [InlineData("ALTER TABLE t ADD a int")]
    [InlineData("GRANT ALL ON t TO public")]
    [InlineData("COPY t FROM '/etc/passwd'")]
    [InlineData("CALL do_something()")]
    [InlineData("DO $$ BEGIN END $$")]
    [InlineData("SET ROLE postgres")]
    [InlineData("SELECT 1; DROP TABLE t")]
    [InlineData("SELECT * FROM t FOR UPDATE")]
    [InlineData("SELECT * INTO copy FROM t")]
    [InlineData("SELECT set_config('default_transaction_read_only', 'off', false)")]
    [InlineData("SELECT nextval('seq')")]
    [InlineData("SELECT pg_advisory_lock(1)")]
    [InlineData("WITH gone AS (DELETE FROM t RETURNING *) SELECT * FROM gone")]
    [InlineData("  \n  update t set a = 1")]
    [InlineData("")]
    public void SupabaseImport_AnythingThatIsNotAPlainSelect_IsRefused(string sql)
    {
        var act = () => ReadOnlySql.Guard(sql);

        act.Should().Throw<ImportException>().WithMessage("*read-only SELECT*");
    }

    [Theory]
    [InlineData("SELECT \"update\", \"delete me\" FROM \"drop\".\"insert\" WHERE x = 'DROP TABLE;'")]
    [InlineData("select a from t where b > $1 order by a limit $2")]
    [InlineData("WITH x AS (SELECT 1) SELECT * FROM x")]
    public void SupabaseImport_QuotedNamesAndLiterals_DoNotTripTheGuard(string sql)
    {
        ReadOnlySql.Guard(sql).Should().Be(sql);
    }

    [Fact]
    public async Task SupabaseImport_RowQueries_QuoteEveryIdentifierAndPassTheKeyAsParameters()
    {
        var runner = new FakeSqlRunner();
        var table = new SupabaseTableRef("public", "we\"ird", [SupabaseImporterTests.Col("t", "k1", "int4"), SupabaseImporterTests.Col("t", "k2", "uuid"), SupabaseImporterTests.Col("t", "doc", "jsonb"), SupabaseImporterTests.Col("t", "at", "timestamptz"), SupabaseImporterTests.Col("t", "raw", "bytea")],
            [new("k1", "pg_catalog", "int4"), new("k2", "pg_catalog", "uuid")]);

        await new PostgresSupabaseSource(runner, "db").ReadRowsAsync(table, ["5", "abc"], 100);

        var (sql, parameters) = runner.Calls.Single();
        sql.Should().Be("SELECT \"k1\"::text, \"k2\"::text, to_jsonb(\"doc\")::text, (\"at\" AT TIME ZONE 'UTC')::text, encode(\"raw\", 'base64'), \"k1\"::text, \"k2\"::text " +
                        "FROM ONLY \"public\".\"we\"\"ird\" WHERE (\"k1\", \"k2\") > ($1::\"pg_catalog\".\"int4\", $2::\"pg_catalog\".\"uuid\") ORDER BY \"k1\", \"k2\" LIMIT $3");
        parameters.Should().Equal("5", "abc", 100);
        sql.Should().NotContain("abc");
    }

    [Fact]
    public async Task SupabaseImport_FirstRowQuery_HasNoKeyFilter()
    {
        var runner = new FakeSqlRunner();
        var table = new SupabaseTableRef("public", "t", [SupabaseImporterTests.Col("t", "id", "int4")], [new("id", "pg_catalog", "int4")]);

        await new PostgresSupabaseSource(runner, "db").ReadRowsAsync(table, null, 50);

        runner.Calls.Single().Sql.Should().Be("SELECT \"id\"::text, \"id\"::text FROM ONLY \"public\".\"t\" ORDER BY \"id\" LIMIT $1");
    }

    [Fact]
    public void SupabaseImport_Sessions_AreOpenedReadOnlyAndFailIfTheServerDisagrees()
    {
        ReadOnlySql.ShowReadOnly.Should().Be("SHOW transaction_read_only");

        ReadOnlySql.VerifyReadOnly("on");
        foreach (var other in new object?[] { "off", null, false, "" })
            new Action(() => ReadOnlySql.VerifyReadOnly(other)).Should().Throw<ImportException>().WithMessage("*not read-only*");
    }

    [Fact]
    public void SupabaseImport_NeverWritesToSupabase_TheSourceInterfaceOffersOnlyReads()
    {
        var verbs = typeof(ISupabaseSource).GetMethods().Where(m => !m.IsSpecialName).Select(m => m.Name).ToList();

        verbs.Should().OnlyContain(n => n.StartsWith("List") || n.StartsWith("Count") || n.StartsWith("Read"));
        typeof(ISqlRunner).GetMethods().Select(m => m.Name).Should().Equal("QueryAsync");
    }

    [Fact]
    public void SupabaseImport_PostgresRows_AreMappedToTypedRecords()
    {
        var runner = new FakeSqlRunner((sql, _) => sql switch
        {
            PostgresSupabaseSource.ColumnsSql => [["public", "t", "a", "integer", "pg_catalog", "int4", null, true, "r"], ["public", "t", "b", "character varying", "pg_catalog", "varchar", 30, false, "p"]],
            PostgresSupabaseSource.ConstraintsSql => [["p", "public", "t", "t_pkey", new[] { "a" }, null, null, Array.Empty<string>()], ["f", "public", "t", "fk", new[] { "b" }, "public", "u", new[] { "id" }]],
            PostgresSupabaseSource.EnumsSql => [["public", "mood", "ok"], ["public", "mood", "bad"]],
            PostgresSupabaseSource.PoliciesSql => [["public", "t", "p", "PERMISSIVE", new[] { "anon" }, "ALL", "true", null]],
            PostgresSupabaseSource.PublicationsSql => [["pub", 3]],
            PostgresSupabaseSource.BucketsSql => [["b", "b", true]],
            PostgresSupabaseSource.ObjectsSql => [["b", "o"]],
            _ => []
        });
        var source = new PostgresSupabaseSource(runner, "db");

        source.ListColumnsAsync(Schemas).Result.Should().Equal(
            new SupabaseColumn("public", "t", "a", "integer", "pg_catalog", "int4", null, true),
            new SupabaseColumn("public", "t", "b", "character varying", "pg_catalog", "varchar", 30, false, 'p'));
        source.ListConstraintsAsync(Schemas).Result.Select(c => (c.Kind, c.Columns.Single(), c.RefTable)).Should().Equal(('p', "a", null), ('f', "b", "u"));
        source.ListEnumsAsync().Result.Single().Labels.Should().Equal("ok", "bad");
        source.ListPoliciesAsync(Schemas).Result.Single().Roles.Should().Equal("anon");
        source.ListPublicationsAsync().Result.Single().Tables.Should().Be(3);
        source.ListBucketsAsync().Result.Single().Public.Should().BeTrue();
        source.ListObjectsAsync("a", "b", 5).Result.Single().Should().Be(new SupabaseObject("b", "o"));
        runner.Calls.Last().Parameters.Should().Equal("a", "b", 5);
    }

    [Fact]
    public void SupabaseImport_SchemaListsAreSentAsTextArrays()
    {
        var runner = new FakeSqlRunner();

        new PostgresSupabaseSource(runner, "db").ListColumnsAsync(["public", "app"]).Wait();

        runner.Calls.Single().Parameters.Single().Should().BeOfType<string[]>().Which.Should().Equal("public", "app");
    }
}

public class SupabaseSecretSafetyTests
{
    private const string Password = "pa55/w0rd?#@x";
    private const string ServiceKey = "eyJ-SERVICE-ROLE-KEY-9f8e7d";
    private static readonly string ConnectionString = $"postgresql://postgres.abcd:{Uri.EscapeDataString(Password)}@aws-0-eu-west-2.pooler.supabase.com:5432/postgres";
    private static readonly string[] Leaks = [Password, Uri.EscapeDataString(Password), ServiceKey, ConnectionString];

    private static SupabaseSecrets Secrets() => new(ConnectionString, SupabaseConnection.Parse(ConnectionString).Password, ServiceKey);

    private static FakeSupabaseSource Leaky(string leak)
    {
        var source = SupabaseImporterTests.Todos();
        var failure = new InvalidOperationException($"failed with {leak}");
        source.AuthFailure = failure;
        source.PolicyFailure = failure;
        source.ObjectsFailure = failure;
        source.RowsFailure = failure;
        return source;
    }

    public static IEnumerable<object[]> LeakCases() => Leaks.Select(l => new object[] { l });

    // ── Rule: the password and service key never appear in plans, warnings, errors or logs ──

    [Theory]
    [MemberData(nameof(LeakCases))]
    public async Task SupabaseImport_SecretsNeverAppearInPlanOrErrors(string leak)
    {
        var importer = new SupabaseImporter(Leaky(leak), Secrets(), "https://abcd.supabase.co", ServiceKey);

        var schema = await importer.FetchSchemaAsync(false, true, false);
        var records = () => importer.FetchRecordsAsync("todos", 1, 10);

        var everything = string.Join('\n', schema.Warnings!.Concat(schema.Errors!));
        everything.Should().Contain("[redacted]");
        foreach (var secret in Leaks) everything.Should().NotContain(secret);
        var thrown = (await records.Should().ThrowAsync<ImportException>()).Which.Message;
        thrown.Should().Contain("[redacted]");
        foreach (var secret in Leaks) thrown.Should().NotContain(secret);
    }

    [Fact]
    public async Task SupabaseImport_SecretsNeverAppearInTheSavedPlan()
    {
        var importer = new SupabaseImporter(Leaky(Password), Secrets(), "https://abcd.supabase.co", ServiceKey);
        var schema = await importer.FetchSchemaAsync(false, true, false);

        var plan = ImportPlanner.Build(schema, TargetSnapshot.Empty, new PlanOptions(false, true, true, false),
            new PlanContext(new PlanSource(importer.PlatformName, importer.ConnectionSummary, ["schema"]),
                new PlanTarget("https://api.example.com", "1"), "run", DateTimeOffset.UnixEpoch));

        var json = PlanJson.Serialise(plan);
        foreach (var secret in Leaks) json.Should().NotContain(secret);
        json.Should().NotContain("pooler.supabase.com").And.NotContain("postgres.abcd");
    }

    [Fact]
    public void SupabaseImport_ConnectionSummaryAndDescription_NeverCarryACredential()
    {
        var connection = SupabaseConnection.Parse(ConnectionString);

        connection.Description.Should().Be("postgres://aws-0-eu-west-2.pooler.supabase.com:5432/postgres");
        connection.Password.Should().Be(Password);
        connection.Description.Should().NotContain(Password).And.NotContain("postgres.abcd");
    }

    [Fact]
    public async Task SupabaseImport_ARealConnectionFailure_DoesNotEchoThePassword()
    {
        var importer = new SupabaseImporter("postgresql://postgres:s3cr3tPW9z@127.0.0.1:1/postgres?sslmode=disable");

        var act = () => importer.FetchSchemaAsync(false, false, false);

        var message = (await act.Should().ThrowAsync<ImportException>()).Which.Message;
        message.Should().NotContain("s3cr3tPW9z");
    }

    [Theory]
    [InlineData("not a connection string")]
    [InlineData("postgresql://u:p@:5432/db")]
    [InlineData("postgresql://u:SECRETPW@h:5432/db?sslmode=bogus")]
    [InlineData("Host=h;Password=SECRETPW;Bogus Option=1")]
    public void SupabaseImport_AnInvalidConnectionString_IsRefusedWithoutEchoingIt(string value)
    {
        var act = () => new SupabaseImporter(value);

        act.Should().Throw<ImportException>().Which.Message.Should().Contain("connection string").And.NotContain("SECRETPW");
    }

    [Fact]
    public void SupabaseImport_KeyValueConnectionStrings_AreAccepted()
    {
        var connection = SupabaseConnection.Parse("Host=db.example.com;Port=6543;Database=app;Username=u;Password=hunter2");

        connection.Description.Should().Be("postgres://db.example.com:6543/app");
        connection.Password.Should().Be("hunter2");
    }

    [Theory]
    [InlineData("require")]
    [InlineData("disable")]
    [InlineData("verify-full")]
    public void SupabaseImport_SslModeInTheUri_IsHonoured(string mode)
    {
        var connection = SupabaseConnection.Parse($"postgresql://u:p@h:5432/db?sslmode={mode}");

        connection.ConnectionString.Replace(" ", "").ToLowerInvariant().Should().Contain("sslmode=" + mode.Replace("-", ""));
    }

    [Fact]
    public void SupabaseSecrets_RedactEveryFormOfEverySecret_LongestFirst()
    {
        var secrets = new SupabaseSecrets("abcdef", "abc", "", null);

        secrets.Redact("a abcdef b abc c").Should().Be("a [redacted] b [redacted] c");
        new SupabaseSecrets("p@ss/word").Redact("x p%40ss%2Fword y p@ss/word").Should().Be("x [redacted] y [redacted]");
    }

    [Fact]
    public void SupabaseImport_TheDownloadUrl_NeverContainsTheServiceKey()
    {
        var importer = new SupabaseImporter(SupabaseImporterTests.Todos(), Secrets(), "https://abcd.supabase.co", ServiceKey);

        importer.GetFileDownloadUrl("pics/a.png").Should().NotContain(ServiceKey);
        importer.SourceAuthToken.Should().Be(ServiceKey);
    }

    [Fact]
    public void SupabaseImport_TheSettingsHelp_RecommendsTheEnvironmentAndABypassRlsRole()
    {
        string Help(string property) => typeof(Commands.ImportSupabaseSettings).GetProperty(property)!
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).Cast<System.ComponentModel.DescriptionAttribute>().Single().Description;

        Help("Db").Should().Contain("SUPABASE_DB_URL").And.Contain("BYPASSRLS");
        Help("ServiceKey").Should().Contain("SUPABASE_SERVICE_KEY");
    }
}
