using System.Text.Json.Nodes;
using Anythink.ProjectImporters;
using Anythink.ProjectImporters.Engine;
using Anythink.ProjectImporters.Supabase;
using AnythinkCli.Client;
using FluentAssertions;

namespace AnythinkCli.Tests;

internal sealed class FakeSupabaseSource : ISupabaseSource
{
    public List<SupabaseColumn> Columns { get; } = [];
    public List<SupabaseConstraint> Constraints { get; } = [];
    public List<SupabaseSkippedTable> Skipped { get; } = [];
    public Func<string?, string?> CursorOf { get; set; } = cell => cell;
    public List<SupabaseEnum> Enums { get; } = [];
    public List<SupabasePolicy> Policies { get; } = [];
    public List<string> Views { get; } = [];
    public List<SupabaseRoutine> Routines { get; } = [];
    public List<SupabaseTrigger> Triggers { get; } = [];
    public List<string> Extensions { get; } = ["plpgsql"];
    public List<SupabasePublication> Publications { get; } = [];
    public List<SupabaseBucket> Buckets { get; } = [];
    public List<SupabaseObject> Objects { get; } = [];
    public Dictionary<string, List<string?[]>> Rows { get; } = [];
    public long CronJobs { get; set; }
    public long AuthUsers { get; set; }
    public Exception? AuthFailure { get; set; }
    public Exception? PolicyFailure { get; set; }
    public Exception? ObjectsFailure { get; set; }
    public Exception? RowsFailure { get; set; }
    public List<IReadOnlyList<string?>?> Cursors { get; } = [];
    public SupabaseTableRef? LastTable { get; private set; }

    public string Description => "postgres://db.example.com:5432/postgres";

    public Task<IReadOnlyList<SupabaseColumn>> ListColumnsAsync(IReadOnlyList<string> schemas) =>
        Task.FromResult<IReadOnlyList<SupabaseColumn>>(Columns.Where(c => schemas.Contains(c.Schema)).ToList());

    public Task<IReadOnlyList<SupabaseSkippedTable>> ListSkippedTablesAsync(IReadOnlyList<string> schemas) =>
        Task.FromResult<IReadOnlyList<SupabaseSkippedTable>>(Skipped);

    public Task<IReadOnlyList<SupabaseConstraint>> ListConstraintsAsync(IReadOnlyList<string> schemas) =>
        Task.FromResult<IReadOnlyList<SupabaseConstraint>>(Constraints.Where(c => schemas.Contains(c.Schema)).ToList());

    public Task<IReadOnlyList<SupabaseEnum>> ListEnumsAsync() => Task.FromResult<IReadOnlyList<SupabaseEnum>>(Enums);

    public Task<IReadOnlyList<SupabasePolicy>> ListPoliciesAsync(IReadOnlyList<string> schemas) =>
        PolicyFailure is null ? Task.FromResult<IReadOnlyList<SupabasePolicy>>(Policies) : throw PolicyFailure;

    public Task<IReadOnlyList<string>> ListViewsAsync(IReadOnlyList<string> schemas) => Task.FromResult<IReadOnlyList<string>>(Views);
    public Task<IReadOnlyList<SupabaseRoutine>> ListRoutinesAsync(IReadOnlyList<string> schemas) => Task.FromResult<IReadOnlyList<SupabaseRoutine>>(Routines);
    public Task<IReadOnlyList<SupabaseTrigger>> ListTriggersAsync(IReadOnlyList<string> schemas) => Task.FromResult<IReadOnlyList<SupabaseTrigger>>(Triggers);
    public Task<IReadOnlyList<string>> ListExtensionsAsync() => Task.FromResult<IReadOnlyList<string>>(Extensions);
    public Task<IReadOnlyList<SupabasePublication>> ListPublicationsAsync() => Task.FromResult<IReadOnlyList<SupabasePublication>>(Publications);
    public Task<long> CountCronJobsAsync() => Task.FromResult(CronJobs);
    public Task<long> CountAuthUsersAsync() => AuthFailure is null ? Task.FromResult(AuthUsers) : throw AuthFailure;
    public Task<IReadOnlyList<SupabaseBucket>> ListBucketsAsync() => Task.FromResult<IReadOnlyList<SupabaseBucket>>(Buckets);

    public Task<IReadOnlyList<SupabaseObject>> ListObjectsAsync(string? afterBucket, string? afterName, int limit)
    {
        if (ObjectsFailure is not null) throw ObjectsFailure;
        var page = Objects.OrderBy(o => o.Bucket, StringComparer.Ordinal).ThenBy(o => o.Name, StringComparer.Ordinal)
            .Where(o => afterBucket is null || string.CompareOrdinal(o.Bucket + "\0" + o.Name, afterBucket + "\0" + afterName) > 0)
            .Take(limit).ToList();
        return Task.FromResult<IReadOnlyList<SupabaseObject>>(page);
    }

    public Task<IReadOnlyList<string?[]>> ReadRowsAsync(SupabaseTableRef table, IReadOnlyList<string?>? afterKey, int limit)
    {
        if (RowsFailure is not null) throw RowsFailure;
        Cursors.Add(afterKey);
        LastTable = table;
        var keyAt = table.Key.Select(k => table.Columns.ToList().FindIndex(c => c.Name == k.Name)).ToList();
        string Key(string?[] r) => string.Join("\0", keyAt.Select(i => CursorOf(r[i])));
        var page = Rows[$"{table.Schema}.{table.Table}"].OrderBy(Key, StringComparer.Ordinal)
            .Where(r => afterKey is null || string.CompareOrdinal(Key(r), string.Join("\0", afterKey)) > 0).Take(limit)
            .Select(r => r.Concat(keyAt.Select(i => CursorOf(r[i]))).ToArray()).ToList();
        return Task.FromResult<IReadOnlyList<string?[]>>(page);
    }
}

public class SupabaseImporterTests
{
    private const string Org = "/org/99999";

    internal static SupabaseColumn Col(string table, string name, string udt, string? dataType = null, int? maxLength = null, string schema = "public") =>
        new(schema, table, name, dataType ?? udt, "pg_catalog", udt, maxLength, true);

    internal static SupabaseConstraint Pk(string table, params string[] columns) =>
        new('p', "public", table, $"{table}_pkey", columns, null, null, []);

    internal static SupabaseConstraint Fk(string table, string column, string refTable, string refColumn = "id") =>
        new('f', "public", table, $"{table}_{column}_fkey", [column], "public", refTable, [refColumn]);

    internal static SupabaseImporter Importer(FakeSupabaseSource source, string? url = "https://abcd.supabase.co", string? key = "service-key-value", params string[] schemas) =>
        new(source, new SupabaseSecrets("service-key-value"), url, key, new SupabaseImportOptions(schemas));

    internal static FakeSupabaseSource Todos()
    {
        var source = new FakeSupabaseSource();
        source.Columns.AddRange([Col("todos", "id", "uuid"), Col("todos", "title", "text"), Col("todos", "done", "bool")]);
        source.Constraints.Add(Pk("todos", "id"));
        source.Rows["public.todos"] = [["11111111-1111-1111-1111-111111111111", "Write tests", "t"], ["22222222-2222-2222-2222-222222222222", "Ship", "f"]];
        return source;
    }

    private static async Task<ImportSchema> Schema(FakeSupabaseSource source, bool files = false, string? url = "https://abcd.supabase.co", string? key = "service-key-value") =>
        await Importer(source, url, key).FetchSchemaAsync(false, files, false);

    private static ImportFieldSpec Field(ImportSchema schema, string entity, string field) =>
        schema.Collections.Single(c => c.Name == entity).Fields.Single(f => f.Name == field);

    // ── Rule: each Postgres type maps onto a fixed Anythink field type ──

    public static IEnumerable<object?[]> TypeMappings() =>
    [
        ["int2", null, null, "integer", "input"],
        ["int4", null, null, "integer", "input"],
        ["int8", null, null, "bigint", "input"],
        ["numeric", null, null, "decimal", "input"],
        ["float4", null, null, "decimal", "input"],
        ["float8", null, null, "decimal", "input"],
        ["bool", null, null, "boolean", "checkbox"],
        ["text", null, null, "text", "textarea"],
        ["citext", null, null, "text", "textarea"],
        ["varchar", null, 40, "varchar", "input"],
        ["varchar", null, 5000, "text", "textarea"],
        ["varchar", null, null, "text", "textarea"],
        ["bpchar", null, 2, "varchar", "input"],
        ["uuid", null, null, "varchar", "input"],
        ["inet", null, null, "varchar", "input"],
        ["date", null, null, "date", "short-date"],
        ["timestamp", null, null, "timestamp", "timestamp"],
        ["timestamptz", null, null, "timestamp", "timestamp"],
        ["json", null, null, "jsonb", "jsonb"],
        ["jsonb", null, null, "jsonb", "jsonb"],
        ["_text", "ARRAY", null, "jsonb", "jsonb"],
        ["_int4", "ARRAY", null, "jsonb", "jsonb"],
        ["bytea", null, null, "text", "textarea"],
        ["time", null, null, "varchar", "input"],
        ["interval", null, null, "varchar", "input"],
        ["money", null, null, "varchar", "input"],
        ["geometry", "USER-DEFINED", null, "text", "textarea"],
    ];

    [Theory]
    [MemberData(nameof(TypeMappings))]
    public async Task SupabaseImport_PostgresType_MapsToTheDocumentedFieldType(string udt, string? dataType, int? maxLength, string db, string display)
    {
        var source = Todos();
        source.Columns.Add(Col("todos", "value", udt, dataType, maxLength));

        var field = Field(await Schema(source), "todos", "value");

        field.DatabaseType.Should().Be(db);
        field.DisplayType.Should().Be(display);
        field.IsRequired.Should().BeFalse();
    }

    [Fact]
    public async Task SupabaseImport_TypesWithNoMatch_AreKeptAsTextAndReported()
    {
        var source = Todos();
        source.Columns.AddRange([Col("todos", "shape", "geometry", "USER-DEFINED"), Col("todos", "at", "time"), Col("todos", "raw", "bytea")]);

        var schema = await Schema(source);

        schema.Warnings.Should().Contain(w => w.StartsWith("public.todos.shape:") && w.Contains("text form"));
        schema.Warnings.Should().Contain(w => w.StartsWith("public.todos.at:") && w.Contains("stored as text"));
        schema.Warnings.Should().Contain(w => w.StartsWith("public.todos.raw:") && w.Contains("base64"));
    }

    [Fact]
    public async Task SupabaseImport_EnumColumns_BecomeTextAndTheAllowedValuesAreListed()
    {
        var source = Todos();
        source.Columns.Add(new SupabaseColumn("public", "todos", "mood", "USER-DEFINED", "public", "mood", null, true));
        source.Enums.Add(new SupabaseEnum("public", "mood", ["happy", "sad"]));

        var schema = await Schema(source);

        Field(schema, "todos", "mood").DatabaseType.Should().Be("varchar");
        schema.Warnings.Should().Contain(w => w.StartsWith("public.todos.mood:") && w.Contains("happy, sad"));
    }

    // ── Rule: primary keys of any type are kept in supabase_id and given a stable record id ──

    [Fact]
    public async Task SupabaseImport_UuidPrimaryKey_IsKeptInAUniqueSupabaseIdFieldNotAsId()
    {
        var schema = await Schema(Todos());

        var id = Field(schema, "todos", "supabase_id");
        id.IsUnique.Should().BeTrue();
        id.DatabaseType.Should().Be("varchar");
        schema.Collections.Single().Fields.Select(f => f.Name).Should().NotContain("id").And.Contain("supabase_extra");
    }

    [Fact]
    public async Task SupabaseImport_Records_CarryTheirKeyAndAStableIdThatForeignKeysResolveTo()
    {
        var source = Todos();
        source.Columns.AddRange([Col("notes", "id", "int8"), Col("notes", "todo_id", "uuid"), Col("notes", "body", "text")]);
        source.Constraints.AddRange([Pk("notes", "id"), Fk("notes", "todo_id", "todos")]);
        source.Rows["public.notes"] = [["7", "11111111-1111-1111-1111-111111111111", "hi"]];
        var importer = Importer(source);
        var schema = await importer.FetchSchemaAsync(false, false, false);

        var todo = (await importer.FetchRecordsAsync("todos", 1, 10)).Records[0];
        var note = (await importer.FetchRecordsAsync("notes", 1, 10)).Records[0];

        todo["supabase_id"]!.GetValue<string>().Should().Be("11111111-1111-1111-1111-111111111111");
        todo["done"]!.GetValue<bool>().Should().BeTrue();
        note["todo_id"]!.GetValue<long>().Should().Be(todo["id"]!.GetValue<long>());
        note["supabase_id"]!.GetValue<string>().Should().Be("7");
        Field(schema, "notes", "todo_id").ForeignKeyCollection.Should().Be("todos");
    }

    [Fact]
    public async Task SupabaseImport_CompositePrimaryKey_IsKeptAsAJsonArrayAndItsColumnsStayFields()
    {
        var source = new FakeSupabaseSource();
        source.Columns.AddRange([Col("members", "team", "int4"), Col("members", "person", "uuid")]);
        source.Constraints.Add(Pk("members", "team", "person"));
        source.Rows["public.members"] = [["1", "aaaa"]];
        var importer = Importer(source);
        var schema = await importer.FetchSchemaAsync(false, false, false);

        var record = (await importer.FetchRecordsAsync("members", 1, 10)).Records[0];

        record["supabase_id"]!.GetValue<string>().Should().Be("""["1","aaaa"]""");
        schema.Collections.Single().Fields.Select(f => f.Name).Should().Contain(["team", "person"]);
    }

    [Fact]
    public async Task SupabaseImport_TableWithoutAPrimaryKey_IsSchemaOnly()
    {
        var source = new FakeSupabaseSource();
        source.Columns.Add(Col("logs", "line", "text"));

        var schema = await Schema(source);

        schema.Collections.Single().DataUnsupportedReason.Should().Contain("no primary key");
        schema.Collections.Single().Fields.Select(f => f.Name).Should().NotContain("supabase_id");
    }

    // ── Rule: foreign keys become relationships only where they can be resolved ──

    [Fact]
    public async Task SupabaseImport_ForeignKeyToAnImportedTable_BecomesARelationship()
    {
        var source = Todos();
        source.Columns.AddRange([Col("notes", "id", "int8"), Col("notes", "todo_id", "uuid")]);
        source.Constraints.AddRange([Pk("notes", "id"), Fk("notes", "todo_id", "todos")]);

        var field = Field(await Schema(source), "notes", "todo_id");

        field.ForeignKeyCollection.Should().Be("todos");
        field.DatabaseType.Should().Be("integer");
    }

    [Fact]
    public async Task SupabaseImport_ForeignKeyToATableThatIsNotImported_KeepsItsValueAndIsReported()
    {
        var source = Todos();
        source.Columns.Add(Col("todos", "owner", "uuid"));
        source.Constraints.Add(new('f', "public", "todos", "todos_owner_fkey", ["owner"], "auth", "users", ["id"]));

        var schema = await Schema(source);

        Field(schema, "todos", "owner").ForeignKeyCollection.Should().BeNull();
        Field(schema, "todos", "owner").DatabaseType.Should().Be("varchar");
        schema.Warnings.Should().Contain(w => w.StartsWith("public.todos.owner:") && w.Contains("auth.users"));
    }

    [Fact]
    public async Task SupabaseImport_CompositeForeignKey_IsReportedAndItsColumnsKept()
    {
        var source = Todos();
        source.Columns.AddRange([Col("todos", "a", "int4"), Col("todos", "b", "int4")]);
        source.Constraints.Add(new('f', "public", "todos", "fk", ["a", "b"], "public", "todos", ["a", "b"]));

        var schema = await Schema(source);

        schema.Warnings.Should().Contain(w => w.StartsWith("public.todos:") && w.Contains("(a, b)") && w.Contains("several columns"));
        schema.Collections.Single().Fields.Select(f => f.Name).Should().Contain(["a", "b"]);
    }

    [Fact]
    public async Task SupabaseImport_SelfReference_KeepsTheOriginalValueInExtra()
    {
        var source = new FakeSupabaseSource();
        source.Columns.AddRange([Col("nodes", "id", "int8"), Col("nodes", "parent", "int8")]);
        source.Constraints.AddRange([Pk("nodes", "id"), Fk("nodes", "parent", "nodes")]);
        source.Rows["public.nodes"] = [["1", null], ["2", "1"]];
        var importer = Importer(source);
        await importer.FetchSchemaAsync(false, false, false);

        var records = (await importer.FetchRecordsAsync("nodes", 1, 10)).Records;

        records[1]["supabase_extra"]!["parent"]!.GetValue<string>().Should().Be("1");
        records[1]["parent"]!.GetValue<long>().Should().Be(records[0]["id"]!.GetValue<long>());
    }

    // ── Rule: names follow the API's rules and every change is visible ──

    [Theory]
    [InlineData("Todos", "todos")]
    [InlineData("UserProfile", "user_profile")]
    [InlineData("order-items", "order_items")]
    [InlineData("1st", "supabase_1st")]
    [InlineData("anythink_x", "supabase_anythink_x")]
    public async Task SupabaseImport_TableNames_AreConvertedAndTheRenameIsReported(string table, string expected)
    {
        var source = new FakeSupabaseSource();
        source.Columns.Add(Col(table, "x", "text"));

        var schema = await Schema(source);

        schema.Collections.Single().Name.Should().Be(expected);
        schema.Warnings.Should().Contain(w => w.Contains($"'public.{table}' is imported as '{expected}'"));
    }

    [Fact]
    public async Task SupabaseImport_TablesThatClashOrCannotBeNamed_AreErrorsNotSilentlyDropped()
    {
        var source = new FakeSupabaseSource();
        source.Columns.AddRange([Col("Items", "x", "text"), Col("items", "x", "text"), Col("___", "x", "text")]);

        var schema = await Schema(source);

        schema.Collections.Should().ContainSingle();
        schema.Errors.Should().HaveCount(2).And.OnlyContain(e => e.Contains("skipped"));
    }

    [Fact]
    public async Task SupabaseImport_ColumnsNamedLikeReservedFields_AreRenamedNotDropped()
    {
        var source = Todos();
        source.Columns.AddRange([Col("todos", "created_at", "timestamptz"), Col("todos", "locked", "bool")]);

        var schema = await Schema(source);

        Field(schema, "todos", "supabase_created_at").DatabaseType.Should().Be("timestamp");
        Field(schema, "todos", "supabase_locked");
        schema.Warnings.Should().Contain(w => w.StartsWith("public.todos:") && w.Contains("created_at -> supabase_created_at"));
    }

    [Theory]
    [InlineData("1abc")]
    [InlineData("###")]
    [InlineData("anythink_secret")]
    public async Task SupabaseImport_ColumnNamesTheApiRejects_GoToExtraWithAWarning(string column)
    {
        var source = Todos();
        source.Columns.Add(Col("todos", column, "text"));
        source.Rows["public.todos"] = [["11111111-1111-1111-1111-111111111111", "t", "t", "kept"]];
        var importer = Importer(source);

        var schema = await importer.FetchSchemaAsync(false, false, false);
        var record = (await importer.FetchRecordsAsync("todos", 1, 10)).Records.Single();

        schema.Warnings.Should().Contain(w => w.Contains($"column '{column}'") && w.Contains("supabase_extra"));
        schema.Collections.Single().Fields.Select(f => f.Name).Should().NotContain(column);
        record["supabase_extra"]![column]!.GetValue<string>().Should().Be("kept");
    }

    [Fact]
    public async Task SupabaseImport_ColumnsThatClashAfterConversion_KeepTheSecondInExtra()
    {
        var source = Todos();
        source.Columns.AddRange([Col("todos", "userId", "text"), Col("todos", "user_id", "text")]);

        var schema = await Schema(source);

        schema.Collections.Single().Fields.Count(f => f.Name == "user_id").Should().Be(1);
        schema.Warnings.Should().Contain(w => w.Contains("column 'user_id'") && w.Contains("clashes"));
    }

    [Fact]
    public async Task SupabaseImport_NamesLongerThanTheLimit_AreNotAccepted()
    {
        var source = Todos();
        source.Columns.Add(Col("todos", new string('a', 64), "text"));

        var schema = await Schema(source);

        schema.Warnings.Should().Contain(w => w.Contains("cannot be a field of its own"));
    }

    // ── Rule: other schemas are opt-in, prefixed, and Supabase's own are refused ──

    [Fact]
    public async Task SupabaseImport_OtherSchemas_AreImportedWithTheSchemaAsAPrefix()
    {
        var source = Todos();
        source.Columns.Add(Col("jobs", "id", "int4", schema: "app"));
        source.Constraints.Add(new('p', "app", "jobs", "jobs_pkey", ["id"], null, null, []));

        var schema = await Importer(source, schemas: ["public", "app"]).FetchSchemaAsync(false, false, false);

        schema.Collections.Select(c => c.Name).Should().BeEquivalentTo("todos", "app_jobs");
    }

    [Theory]
    [InlineData("auth")]
    [InlineData("storage")]
    [InlineData("realtime")]
    [InlineData("extensions")]
    [InlineData("graphql_public")]
    [InlineData("vault")]
    [InlineData("supabase_functions")]
    [InlineData("pgsodium")]
    [InlineData("pg_temp")]
    [InlineData("information_schema")]
    public void SupabaseImport_SupabaseOwnSchemas_AreNeverImported(string schema)
    {
        var act = () => Importer(Todos(), schemas: [schema]);

        act.Should().Throw<ImportException>().WithMessage($"*'{schema}'*never imported*");
    }

    [Fact]
    public async Task SupabaseImport_ASchemaWithNoTables_IsReported()
    {
        var schema = await Importer(Todos(), schemas: ["public", "empty"]).FetchSchemaAsync(false, false, false);

        schema.Warnings.Should().Contain(w => w.Contains("'empty'") && w.Contains("no tables"));
    }

    // ── Rule: a value that does not fit its column goes to extra, never to a failed record ──

    public static IEnumerable<object[]> Unrepresentable() =>
    [
        ["int8", "not a number"],
        ["float8", "NaN"],
        ["float8", "Infinity"],
        ["numeric", "123456789012345678901234567890.5"],
        ["timestamptz", "infinity"],
        ["timestamp", "0044-03-15 00:00:00 BC"],
        ["date", "infinity"],
        ["bool", "maybe"],
        ["jsonb", "{broken"],
    ];

    [Theory]
    [MemberData(nameof(Unrepresentable))]
    public async Task SupabaseImport_ValueThatDoesNotFitItsColumn_GoesToExtraNotAFailedRecord(string udt, string value)
    {
        var source = Todos();
        source.Columns.Add(Col("todos", "v", udt));
        source.Rows["public.todos"] = [["11111111-1111-1111-1111-111111111111", "t", "t", value]];
        var importer = Importer(source);
        await importer.FetchSchemaAsync(false, false, false);

        var record = (await importer.FetchRecordsAsync("todos", 1, 10)).Records.Single();

        record.ContainsKey("v").Should().BeFalse();
        record["supabase_extra"]!["v"]!.GetValue<string>().Should().Be(value);
        importer.LateWarnings.Should().ContainSingle(w => w.StartsWith("todos.v: 1 value(s)") && w.Contains("supabase_extra"));
    }

    public static IEnumerable<object[]> Representable() =>
    [
        ["int8", "9007199254740993", 9007199254740993L],
        ["numeric", "12.50", 12.50m],
        ["float8", "0.1", 0.1],
        ["bool", "t", true],
    ];

    [Theory]
    [MemberData(nameof(Representable))]
    public async Task SupabaseImport_ValueThatFits_IsCopiedWithItsType(string udt, string value, object expected)
    {
        var source = Todos();
        source.Columns.Add(Col("todos", "v", udt));
        source.Rows["public.todos"] = [["11111111-1111-1111-1111-111111111111", "t", "t", value]];
        var importer = Importer(source);
        await importer.FetchSchemaAsync(false, false, false);

        var record = (await importer.FetchRecordsAsync("todos", 1, 10)).Records.Single();

        record["v"]!.ToJsonString().Should().Be(JsonValue.Create(expected)!.ToJsonString());
    }

    [Fact]
    public async Task SupabaseImport_DatesTimesJsonAndArrays_AreCopiedAsJsonValues()
    {
        var source = Todos();
        source.Columns.AddRange([Col("todos", "d", "date"), Col("todos", "t", "timestamp"), Col("todos", "tz", "timestamptz"), Col("todos", "j", "jsonb"), Col("todos", "a", "_text", "ARRAY"), Col("todos", "b", "bytea")]);
        source.Rows["public.todos"] = [["11111111-1111-1111-1111-111111111111", "t", "t", "2024-02-03", "2024-02-03 04:05:06.5", "2024-02-03 04:05:06", """{"a":[1,2]}""", """["x","y"]""", "AQID"]];
        var importer = Importer(source);
        await importer.FetchSchemaAsync(false, false, false);

        var record = (await importer.FetchRecordsAsync("todos", 1, 10)).Records.Single();

        record["d"]!.GetValue<string>().Should().Be("2024-02-03");
        record["t"]!.GetValue<string>().Should().Be("2024-02-03T04:05:06.5000000Z");
        record["tz"]!.GetValue<string>().Should().Be("2024-02-03T04:05:06.0000000Z");
        record["j"]!["a"]![1]!.GetValue<int>().Should().Be(2);
        record["a"]![1]!.GetValue<string>().Should().Be("y");
        record["b"]!.GetValue<string>().Should().Be("AQID");
    }

    [Fact]
    public async Task SupabaseImport_NullValues_AreLeftOut()
    {
        var source = Todos();
        source.Rows["public.todos"] = [["11111111-1111-1111-1111-111111111111", null, null]];
        var importer = Importer(source);
        await importer.FetchSchemaAsync(false, false, false);

        var record = (await importer.FetchRecordsAsync("todos", 1, 10)).Records.Single();

        record.ContainsKey("title").Should().BeFalse();
        record.ContainsKey("supabase_extra").Should().BeFalse();
    }

    [Fact]
    public async Task SupabaseImport_RecordPaging_ContinuesAfterTheLastKeyOfThePreviousPage()
    {
        var source = Todos();
        var importer = Importer(source);
        await importer.FetchSchemaAsync(false, false, false);

        var first = await importer.FetchRecordsAsync("todos", 1, 1);
        var second = await importer.FetchRecordsAsync("todos", 2, 1);
        var third = await importer.FetchRecordsAsync("todos", 3, 1);

        first.Records.Single()["title"]!.GetValue<string>().Should().Be("Write tests");
        second.Records.Single()["title"]!.GetValue<string>().Should().Be("Ship");
        third.Records.Should().BeEmpty();
        source.Cursors.Select(c => c is null ? null : string.Join(",", c)).Should()
            .Equal(null, "11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222");
    }

    [Fact]
    public async Task SupabaseImport_RecordsOfAnUnknownTable_AreRefused()
    {
        var act = () => Importer(Todos()).FetchRecordsAsync("nope", 1, 10);

        await act.Should().ThrowAsync<ImportException>().WithMessage("*'nope'*");
    }

    [Fact]
    public async Task SupabaseImport_FailedRecords_AreNamedBySupabaseKey()
    {
        var importer = Importer(Todos());
        await importer.FetchSchemaAsync(false, false, false);
        var record = (await importer.FetchRecordsAsync("todos", 1, 1)).Records[0];

        importer.RecordLabel(record, 5).Should().Be("11111111-1111-1111-1111-111111111111");
        importer.RecordLabel(new JsonObject(), 5).Should().Be("5");
    }

    // ── Rule: nothing that cannot be brought across is dropped silently ──

    [Fact]
    public async Task SupabaseImport_NeverReadAreas_AreListedInOneFixedWarning()
    {
        var schema = await Schema(Todos());

        schema.Warnings!.First().Should().StartWith("Never read, so not brought across:")
            .And.Contain("auth users").And.Contain("vault").And.Contain("Edge Functions").And.Contain("cron job commands").And.Contain("check constraints");
    }

    [Fact]
    public async Task SupabaseImport_RowLevelSecurityPolicies_AreEachListedAsNotBroughtAcross()
    {
        var source = Todos();
        source.Policies.AddRange([
            new SupabasePolicy("public", "todos", "Owners read", "PERMISSIVE", ["authenticated"], "SELECT", "(auth.uid() =\n  owner)", null),
            new SupabasePolicy("public", "todos", "Owners write", "PERMISSIVE", ["authenticated", "service_role"], "INSERT", null, "(auth.uid() = owner)"),
            new SupabasePolicy("storage", "objects", "Public read", "PERMISSIVE", ["public"], "SELECT", "(bucket_id = 'pics'::text)", null)]);

        var schema = await Schema(source);

        var policies = schema.Warnings!.Where(w => w.StartsWith("row-level security policy")).ToList();
        policies.Should().HaveCount(3).And.OnlyContain(w => w.Contains("Not brought across: set up roles and record access"));
        policies[0].Should().Contain("\"Owners read\" on public.todos (SELECT, permissive, to authenticated): using ((auth.uid() = owner))");
        policies[1].Should().Contain("INSERT").And.Contain("authenticated, service_role").And.Contain("with check ((auth.uid() = owner))");
        policies[2].Should().Contain("storage.objects");
        schema.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task SupabaseImport_PoliciesThatCannotBeRead_AreAnErrorNotSilence()
    {
        var source = Todos();
        source.PolicyFailure = new InvalidOperationException("permission denied");

        var schema = await Schema(source);

        schema.Errors.Should().ContainSingle(e => e.Contains("policies could not be read") && e.Contains("permission denied"));
    }

    [Fact]
    public async Task SupabaseImport_ViewsFunctionsTriggersExtensionsPublicationsAndCron_AreReportedNotImported()
    {
        var source = Todos();
        source.Views.Add("public.open_todos");
        source.Routines.AddRange([new("public", "touch"), new("public", "touch")]);
        source.Triggers.Add(new("public", "todos", "on_change"));
        source.Extensions.AddRange(["pgcrypto", "pg_cron"]);
        source.Publications.Add(new("supabase_realtime", 2));
        source.CronJobs = 3;

        var schema = await Schema(source);

        schema.Warnings.Should().Contain(w => w.StartsWith("1 view(s)") && w.Contains("public.open_todos"));
        schema.Warnings.Should().Contain(w => w.StartsWith("2 database function(s)") && w.Contains("public.touch"));
        schema.Warnings.Should().Contain(w => w.StartsWith("1 trigger(s)") && w.Contains("public.todos.on_change"));
        schema.Warnings.Should().Contain(w => w.StartsWith("2 extension(s)") && w.Contains("pgcrypto") && !w.Contains("plpgsql"));
        schema.Warnings.Should().Contain(w => w.StartsWith("1 publication(s)") && w.Contains("supabase_realtime: 2 table(s)"));
        schema.Warnings.Should().Contain(w => w.StartsWith("3 cron job(s)"));
        schema.Flows.Should().BeEmpty();
        schema.Roles.Should().BeEmpty();
    }

    [Fact]
    public async Task SupabaseImport_LongListsOfNames_AreCountedAndTrimmed()
    {
        var source = Todos();
        source.Views.AddRange(Enumerable.Range(0, 14).Select(i => $"public.v{i}"));

        var schema = await Schema(source);

        schema.Warnings.Should().Contain(w => w.StartsWith("14 view(s)") && w.Contains("and 4 more"));
    }

    [Fact]
    public async Task SupabaseImport_AuthUsers_AreReportedAsACountAndNeverImported()
    {
        var source = Todos();
        source.AuthUsers = 42;

        var schema = await Schema(source);

        schema.Warnings.Should().Contain(w => w.StartsWith("42 auth user(s)") && w.Contains("only the count is read"));
        schema.Collections.Select(c => c.Name).Should().NotContain("users");
    }

    [Fact]
    public async Task SupabaseImport_AuthUsersThatCannotBeCounted_AreReportedWithoutFailingTheImport()
    {
        var source = Todos();
        source.AuthFailure = new InvalidOperationException("permission denied for schema auth");

        var schema = await Schema(source);

        schema.Warnings.Should().Contain(w => w.StartsWith("auth users could not be read") && w.Contains("permission denied"));
        schema.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task SupabaseImport_CheckAndCompositeUniqueConstraints_AreReportedPerTable()
    {
        var source = Todos();
        source.Constraints.AddRange([
            new('c', "public", "todos", "title_not_blank", [], null, null, []),
            new('u', "public", "todos", "title_done", ["title", "done"], null, null, [])]);

        var schema = await Schema(source);

        schema.Warnings.Should().Contain(w => w.StartsWith("public.todos:") && w.Contains("check or exclusion") && w.Contains("title_not_blank"));
        schema.Warnings.Should().Contain(w => w.StartsWith("public.todos:") && w.Contains("unique constraint (title, done)"));
    }

    [Fact]
    public async Task SupabaseImport_SingleColumnUniqueConstraints_BecomeUniqueFields()
    {
        var source = Todos();
        source.Constraints.Add(new('u', "public", "todos", "title_key", ["title"], null, null, []));

        Field(await Schema(source), "todos", "title").IsUnique.Should().BeTrue();
    }

    [Fact]
    public async Task SupabaseImport_ColumnsAreNeverRequired_SoNoRecordFailsOnAConstraint()
    {
        var source = Todos();
        source.Columns.Add(new SupabaseColumn("public", "todos", "n", "integer", "pg_catalog", "int4", null, Nullable: false));

        var schema = await Schema(source);

        schema.Collections.Single().Fields.Should().OnlyContain(f => !f.IsRequired);
    }

    // ── Rule: files and entities are never widened to public ──

    [Fact]
    public async Task SupabaseImport_NewEntitiesAndFiles_AreNeverPublic_AndNoRolesAreCreated()
    {
        var source = Todos();
        source.Buckets.AddRange([new("pics", "pics", true), new("docs", "docs", false)]);
        source.Objects.AddRange([new("pics", "a/b.png"), new("docs", "c.pdf")]);

        var schema = await Schema(source, files: true);

        schema.Collections.Should().OnlyContain(c => !c.IsPublic);
        schema.Files.Should().HaveCount(2).And.OnlyContain(f => !f.IsPublic);
        schema.Roles.Should().BeEmpty();
        schema.Warnings.Should().Contain(w => w.Contains("bucket 'pics' is public") && w.Contains("imported private"));
        schema.Warnings.Should().NotContain(w => w.Contains("bucket 'docs' is public"));
    }

    [Fact]
    public async Task SupabaseImport_StorageObjects_BecomeFilesAndFolderMarkersAreSkipped()
    {
        var source = Todos();
        source.Buckets.Add(new("pics", "pics", false));
        source.Objects.AddRange([new("pics", "a/b.png"), new("pics", "a/.emptyFolderPlaceholder"), new("pics", "folder/")]);

        var schema = await Schema(source, files: true);

        schema.Files.Should().ContainSingle().Which.Should().Be(new ImportFile("pics/a/b.png", "b.png", false));
    }

    [Fact]
    public async Task SupabaseImport_StorageListing_PagesThroughEveryObject()
    {
        var source = Todos();
        source.Objects.AddRange(Enumerable.Range(0, 2500).Select(i => new SupabaseObject("pics", $"f{i:D5}.png")));

        var schema = await Schema(source, files: true);

        schema.Files.Should().HaveCount(2500);
    }

    [Fact]
    public async Task SupabaseImport_FilesAreOnlyListedWhenRequested()
    {
        var source = Todos();
        source.Objects.Add(new("pics", "a.png"));

        (await Schema(source)).Files.Should().BeEmpty();
    }

    [Fact]
    public async Task SupabaseImport_FilesWithoutAProjectUrlAndKey_AreRefusedBeforeAnythingIsRead()
    {
        var act = () => Importer(Todos(), url: null, key: null).FetchSchemaAsync(false, true, false);

        await act.Should().ThrowAsync<ImportException>().WithMessage("*project URL*service role key*");
    }

    [Fact]
    public async Task SupabaseImport_StorageThatCannotBeListed_IsAnError()
    {
        var source = Todos();
        source.ObjectsFailure = new InvalidOperationException("relation storage.objects does not exist");

        var schema = await Schema(source, files: true);

        schema.Errors.Should().ContainSingle(e => e.StartsWith("files: could not list Storage"));
        schema.Files.Should().BeEmpty();
    }

    [Fact]
    public void SupabaseImport_DownloadUrls_UseTheAuthenticatedStorageRouteWithEscapedPaths()
    {
        var importer = Importer(Todos(), url: "https://abcd.supabase.co/");

        importer.GetFileDownloadUrl("my bucket/a b/c#d.png").Should()
            .Be("https://abcd.supabase.co/storage/v1/object/authenticated/my%20bucket/a%20b/c%23d.png");
        importer.SourceAuthToken.Should().Be("service-key-value");
    }

    [Fact]
    public void SupabaseImport_DownloadUrlWithoutAProjectUrl_IsRefused()
    {
        var act = () => Importer(Todos(), url: null, key: null).GetFileDownloadUrl("pics/a.png");

        act.Should().Throw<ImportException>();
    }

    [Fact]
    public void SupabaseImport_ConnectionSummary_NamesTheProjectOrTheHostButNeverACredential()
    {
        Importer(Todos(), url: "https://user:pw@abcd.supabase.co/?apikey=x").ConnectionSummary.Should().Be("https://abcd.supabase.co");
        Importer(Todos(), url: null, key: null).ConnectionSummary.Should().Be("postgres://db.example.com:5432/postgres");
    }

    // ── Rule: applying twice creates nothing new ──

    private static StubHttpHandler EmptyTarget() => new StubHttpHandler()
        .On($"{Org}/entities", "[]")
        .On(HttpMethod.Post, $"{Org}/entities", """{"name":"todos","table_name":"todos","enable_rls":false,"is_system":false,"is_junction":false,"is_public":false,"lock_new_records":false,"fields":[],"id":1}""")
        .On(HttpMethod.Post, $"{Org}/entities/todos/fields", "{}")
        .On($"{Org}/entities/todos/items?page=1&pageSize=1", """{"items":[],"total_items":0}""")
        .On(HttpMethod.Post, $"{Org}/entities/todos/items", """{"id":1}""");

    private static ImportRunner Runner(FakeSupabaseSource source, StubHttpHandler target, bool dryRun = false) =>
        new(Importer(source), new AnythinkClient("99999", "https://api.example.com", new HttpClient(target)),
            new ImportOptions(DryRun: dryRun, IncludeFlows: false, IncludeData: true));

    [Fact]
    public async Task SupabaseImport_Apply_CreatesTheEntityAndOneRecordPerRow()
    {
        var target = EmptyTarget();

        var result = await Runner(Todos(), target).RunAsync();

        result.Errors.Should().BeEmpty();
        result.EntitiesCreated.Should().Be(1);
        result.RecordsCreated.Should().Be(2);
        target.Matching(HttpMethod.Post, "/todos/items").Select(r => JsonNode.Parse(r.Body)!["supabase_id"]!.GetValue<string>())
            .Should().BeEquivalentTo("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222");
        target.Matching(HttpMethod.Post, "/entities").Should().OnlyContain(r => !r.Body.Contains("\"is_public\":true"));
    }

    [Fact]
    public async Task SupabaseImport_RerunAfterApply_CreatesNothingNew()
    {
        var existing = """{"name":"todos","table_name":"todos","enable_rls":false,"is_system":false,"is_junction":false,"is_public":false,"lock_new_records":false,"id":1,"fields":[{"id":1,"name":"supabase_id"},{"id":2,"name":"title"},{"id":3,"name":"done"},{"id":4,"name":"supabase_extra"}]}""";
        var target = new StubHttpHandler()
            .On($"{Org}/entities", $"[{existing}]")
            .On($"{Org}/entities/todos/items?page=1&pageSize=1", """{"items":[{"id":1}],"total_items":2}""");

        var result = await Runner(Todos(), target).RunAsync();

        target.Writes.Should().BeEmpty();
        result.RecordsCreated.Should().Be(0);
        result.Warnings.Should().Contain(w => w.Contains("todos") && w.Contains("already has 2 record"));
    }

    [Fact]
    public async Task SupabaseImport_DryRun_ChangesNothingAndLeavesThePoliciesInThePlan()
    {
        var source = Todos();
        source.Policies.Add(new SupabasePolicy("public", "todos", "Owners read", "PERMISSIVE", ["authenticated"], "SELECT", "true", null));
        var target = new StubHttpHandler().On($"{Org}/entities", "[]");
        var runner = Runner(source, target, dryRun: true);

        var plan = await runner.BuildPlanAsync(await Importer(source).FetchSchemaAsync(false, false, false));

        target.Writes.Should().BeEmpty();
        plan.Items.Should().Contain(i => i.Kind == PlanKind.Report && i.Reason.StartsWith("row-level security policy \"Owners read\" on public.todos (SELECT"));
        plan.Items.Should().Contain(i => i.Kind == PlanKind.Entity && i.Action == PlanAction.Create);
        plan.Source.Url.Should().Be("https://abcd.supabase.co");
    }

    [Fact]
    public async Task SupabaseImport_MismatchWarnings_ReachTheRunnerResultAfterData()
    {
        var source = Todos();
        source.Columns.Add(Col("todos", "n", "float8"));
        source.Rows["public.todos"] = [["11111111-1111-1111-1111-111111111111", "t", "t", "NaN"]];

        var result = await Runner(source, EmptyTarget()).RunAsync();

        result.Warnings.Should().Contain(w => w.StartsWith("todos.n: 1 value(s)"));
        result.RecordsFailed.Should().Be(0);
    }

    [Fact]
    public async Task SupabaseImport_FailedRecords_AreReportedByTheirSupabaseKey()
    {
        var target = EmptyTarget().On(HttpMethod.Post, $"{Org}/entities/todos/items", "boom", System.Net.HttpStatusCode.BadRequest);

        var result = await Runner(Todos(), target).RunAsync();

        result.Errors.Should().Contain(e => e.StartsWith("todos#11111111-1111-1111-1111-111111111111:"));
    }

    [Fact]
    public async Task SupabaseImport_ReadFailures_AreWrappedAsImportErrors()
    {
        var source = Todos();
        source.RowsFailure = new InvalidOperationException("connection reset");
        var importer = Importer(source);
        await importer.FetchSchemaAsync(false, false, false);

        var act = () => importer.FetchRecordsAsync("todos", 1, 10);

        await act.Should().ThrowAsync<ImportException>().WithMessage("*connection reset*");
    }
}
