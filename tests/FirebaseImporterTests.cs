using System.Text.Json;
using System.Text.Json.Nodes;
using Anythink.ProjectImporters;
using Anythink.ProjectImporters.Engine;
using Anythink.ProjectImporters.Firebase;
using AnythinkCli.Client;
using FluentAssertions;

namespace AnythinkCli.Tests;

internal sealed class FakeFirebaseSource : IFirebaseSource
{
    public Dictionary<string, List<FirebaseDocument>> Collections { get; } = [];
    public List<string> Objects { get; } = [];
    public FirebaseUserCounts UserCounts { get; set; } = new(0, 0);
    public Exception? UsersFailure { get; set; }
    public Exception? ObjectsFailure { get; set; }
    public Exception? DocumentsFailure { get; set; }
    public List<string?> Cursors { get; } = [];
    public string ProjectId => "demo-project";

    public Task<IReadOnlyList<string>> ListCollectionIdsAsync() => Task.FromResult<IReadOnlyList<string>>(Collections.Keys.ToList());

    public Task<IReadOnlyList<FirebaseDocument>> SampleDocumentsAsync(string collection, int limit) =>
        Task.FromResult<IReadOnlyList<FirebaseDocument>>(Collections[collection].Take(limit).ToList());

    public Task<IReadOnlyList<FirebaseDocument>> ReadDocumentsAsync(string collection, string? afterPath, int limit)
    {
        if (DocumentsFailure is not null) throw DocumentsFailure;
        Cursors.Add(afterPath);
        var ordered = Collections[collection].OrderBy(d => d.Path, StringComparer.Ordinal)
            .Where(d => afterPath is null || string.CompareOrdinal(d.Path, afterPath) > 0).Take(limit).ToList();
        return Task.FromResult<IReadOnlyList<FirebaseDocument>>(ordered);
    }

    public Task<IReadOnlyList<string>> ListObjectNamesAsync() =>
        ObjectsFailure is null ? Task.FromResult<IReadOnlyList<string>>(Objects) : throw ObjectsFailure;

    public Exception? SignFailure { get; set; }

    public string SignDownloadUrl(string objectName) =>
        SignFailure is null ? "https://storage.example.com/signed/" + objectName : throw SignFailure;

    public Task<FirebaseUserCounts> CountUsersAsync() =>
        UsersFailure is null ? Task.FromResult(UserCounts) : throw UsersFailure;
}

public class FirebaseImporterTests
{
    private const string Org = "/org/99999";

    internal static FirebaseDocument Doc(string path, Dictionary<string, object?> fields, params string[] subcollections) =>
        new(path, fields, subcollections);

    private static FirebaseImporter Importer(FakeFirebaseSource source, int sampleSize = 100) =>
        new(source, text => text, sampleSize);

    private static FakeFirebaseSource Source(string collection, params FirebaseDocument[] docs)
    {
        var source = new FakeFirebaseSource();
        source.Collections[collection] = [.. docs];
        return source;
    }

    private static async Task<ImportSchema> Schema(FakeFirebaseSource source, bool files = false) =>
        await Importer(source).FetchSchemaAsync(false, files, false);

    private static ImportFieldSpec Field(ImportSchema schema, string entity, string field) =>
        schema.Collections.Single(c => c.Name == entity).Fields.Single(f => f.Name == field);

    // ── Rule: each Firestore value type maps onto a fixed Anythink field type ──

    public static IEnumerable<object[]> TypeMappings() =>
    [
        ["hello", "varchar", "input"],
        [new string('x', 300), "text", "textarea"],
        [5L, "integer", "input"],
        [5_000_000_000L, "bigint", "input"],
        [1.5, "decimal", "input"],
        [true, "boolean", "checkbox"],
        [new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), "timestamp", "timestamp"],
        [new Dictionary<string, object?> { ["a"] = 1L }, "jsonb", "jsonb"],
        [new List<object?> { "a", 1L }, "jsonb", "jsonb"],
        [new FirebaseGeoPoint(51.5, -0.1), "jsonb", "jsonb"],
        [new byte[] { 1, 2, 3 }, "text", "textarea"],
    ];

    [Theory]
    [MemberData(nameof(TypeMappings))]
    public async Task FirebaseImport_FirestoreValueType_MapsToTheDocumentedFieldType(object value, string db, string display)
    {
        var schema = await Schema(Source("items", Doc("items/a", new() { ["value"] = value })));

        var field = Field(schema, "items", "value");
        field.DatabaseType.Should().Be(db);
        field.DisplayType.Should().Be(display);
        field.IsRequired.Should().BeFalse();
    }

    [Fact]
    public async Task FirebaseImport_IntegersAndDoublesInOneField_BecomeDecimal()
    {
        var schema = await Schema(Source("items", Doc("items/a", new() { ["n"] = 1L }), Doc("items/b", new() { ["n"] = 1.5 })));

        Field(schema, "items", "n").DatabaseType.Should().Be("decimal");
    }

    [Fact]
    public async Task FirebaseImport_MixedTypes_AreStoredAsJsonAndReported()
    {
        var schema = await Schema(Source("items", Doc("items/a", new() { ["v"] = 1L }), Doc("items/b", new() { ["v"] = "x" })));

        Field(schema, "items", "v").DatabaseType.Should().Be("jsonb");
        schema.Warnings.Should().Contain(w => w.Contains("items.v") && w.Contains("mixed"));
    }

    [Fact]
    public async Task FirebaseImport_ReferenceToAnImportedCollection_BecomesARelationship()
    {
        var source = Source("posts", Doc("posts/p1", new() { ["author"] = new FirebaseReference("users/u1") }));
        source.Collections["users"] = [Doc("users/u1", new() { ["name"] = "Ada" })];

        var schema = await Schema(source);

        var author = Field(schema, "posts", "author");
        author.ForeignKeyCollection.Should().Be("users");
        author.DatabaseType.Should().Be("integer");
    }

    [Fact]
    public async Task FirebaseImport_ReferenceToAnUnknownTarget_IsKeptAsPathTextAndReported()
    {
        var schema = await Schema(Source("posts", Doc("posts/p1", new() { ["author"] = new FirebaseReference("users/u1/notes/n1") })));

        var author = Field(schema, "posts", "author");
        author.ForeignKeyCollection.Should().BeNull();
        author.DatabaseType.Should().Be("varchar");
        schema.Warnings.Should().Contain(w => w.Contains("posts.author") && w.Contains("path text"));
    }

    // ── Rule: names are converted to Anythink's lowercase snake case, and clashes are reported rather than lost ──

    [Theory]
    [InlineData("userProfiles", "user_profiles")]
    [InlineData("HTMLParser", "html_parser")]
    [InlineData("order-items 2", "order_items_2")]
    public void FirebaseImport_Names_AreConvertedToSnakeCase(string source, string expected) =>
        FirebaseFieldMapping.SnakeCase(source).Should().Be(expected);

    [Fact]
    public async Task FirebaseImport_CollectionNamesThatClash_AreReportedAsErrors()
    {
        var source = Source("userProfiles", Doc("userProfiles/a", new()));
        source.Collections["user_profiles"] = [Doc("user_profiles/a", new())];

        var schema = await Schema(source);

        schema.Collections.Should().HaveCount(1);
        schema.Errors.Should().ContainSingle(e => e.Contains("skipped"));
    }

    [Fact]
    public async Task FirebaseImport_FieldsNamedLikeReservedColumns_AreRenamedNotDropped()
    {
        var schema = await Schema(Source("items", Doc("items/a", new() { ["id"] = "x", ["createdAt"] = 1L })));

        var names = schema.Collections.Single().Fields.Select(f => f.Name);
        names.Should().Contain(["firebase_id", "firebase_created_at"]);
        names.Should().NotContain(["id", "created_at"]);
    }

    [Fact]
    public async Task FirebaseImport_FieldsThatClashAfterRenaming_KeepTheirValuesInExtraAndAreReported()
    {
        var source = Source("items", Doc("items/a", new() { ["userId"] = "a", ["user_id"] = "b" }));

        var importer = Importer(source);
        var schema = await importer.FetchSchemaAsync(false, false, false);
        var records = await importer.FetchRecordsAsync("items", 1, 10);

        schema.Warnings.Should().Contain(w => w.Contains("items") && w.Contains("firebase_extra"));
        schema.Collections.Single().Fields.Count(f => f.Name == "user_id").Should().Be(1);
        records.Records.Single()["firebase_extra"]!["user_id"]!.GetValue<string>().Should().Be("b");
    }

    // ── Rule: unmapped things appear in the plan and are never silently dropped ──

    [Fact]
    public async Task FirebaseImport_Subcollections_AreReportedAsUnmapped()
    {
        var schema = await Schema(Source("orders", Doc("orders/o1", new() { ["total"] = 5L }, "items", "events")));

        schema.Warnings.Should().Contain(w => w.StartsWith("orders/items:") && w.Contains("not brought across"));
        schema.Warnings.Should().Contain(w => w.StartsWith("orders/events:"));
        schema.Collections.Should().ContainSingle(c => c.Name == "orders");
    }

    [Fact]
    public async Task FirebaseImport_SecurityRules_AreListedAsNotBroughtAcross()
    {
        var schema = await Schema(new FakeFirebaseSource());

        schema.Warnings.Should().ContainSingle(w => w.Contains("Security Rules") && w.Contains("not brought across"));
    }

    [Fact]
    public async Task FirebaseImport_StripeExtensionData_IsReportedNotImportedAsPayments()
    {
        var schema = await Schema(Source("customers", Doc("customers/c1", new(), "checkout_sessions", "subscriptions")));

        schema.Warnings.Should().Contain(w => w.Contains("Stripe") && w.Contains("payments are not brought across"));
    }

    [Fact]
    public async Task FirebaseImport_AuthenticationUsers_AreReportedAsCountsAndNeverImported()
    {
        var source = new FakeFirebaseSource();
        source.UserCounts = new FirebaseUserCounts(2, 1);

        var schema = await Schema(source);

        var warning = schema.Warnings!.Single(w => w.Contains("Authentication user"));
        warning.Should().Contain("2 Authentication user(s)").And.Contain("1 disabled").And.Contain("password hashes");
        typeof(FirebaseUserCounts).GetProperties().Select(p => p.Name).Should().BeEquivalentTo("Total", "Disabled");
        schema.Collections.Should().BeEmpty();
    }

    [Fact]
    public async Task FirebaseImport_UsersThatCannotBeRead_AreReportedWithoutFailingTheImport()
    {
        var source = Source("items", Doc("items/a", new()));
        source.UsersFailure = new InvalidOperationException("permission denied");

        var schema = await Schema(source);

        schema.Warnings.Should().Contain(w => w.Contains("could not be read") && w.Contains("permission denied"));
        schema.Errors.Should().BeEmpty();
        schema.Collections.Should().ContainSingle();
    }

    // ── Rule: the import never widens access ──

    [Fact]
    public async Task FirebaseImport_NewEntitiesAndFiles_AreNeverPublicAndNoRolesAreCreated()
    {
        var source = Source("items", Doc("items/a", new()));
        source.Objects.Add("img/a.png");

        var schema = await Schema(source, files: true);

        schema.Collections.Should().OnlyContain(c => !c.IsPublic);
        schema.Files.Should().OnlyContain(f => !f.IsPublic);
        schema.Roles.Should().BeEmpty();
    }

    // ── Rule: fields beyond the sample are kept, not dropped ──

    [Fact]
    public async Task FirebaseImport_FieldsOutsideTheSample_AreKeptInTheExtraField()
    {
        var source = Source("items", Doc("items/a", new() { ["seen"] = "x" }), Doc("items/b", new() { ["seen"] = "y", ["rare"] = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero) }));
        var importer = Importer(source, sampleSize: 1);

        var schema = await importer.FetchSchemaAsync(false, false, false);
        var page = await importer.FetchRecordsAsync("items", 1, 10);

        schema.Collections.Single().Fields.Select(f => f.Name).Should().NotContain("rare");
        page.Records.Single(r => r["firebase_path"]!.GetValue<string>() == "items/b")["firebase_extra"]!["rare"]!.GetValue<string>()
            .Should().Be("2026-05-01T00:00:00.0000000Z");
    }

    // ── Rule: files ──

    [Fact]
    public async Task FirebaseImport_StorageObjects_BecomeFiles_AndFolderMarkersAreSkipped()
    {
        var source = new FakeFirebaseSource();
        source.Objects.AddRange(["avatars/", "avatars/ada.png", "top.txt"]);
        var importer = Importer(source);

        var schema = await importer.FetchSchemaAsync(false, true, false);

        schema.Files.Select(f => (f.SourceId, f.FileName)).Should().Equal(("avatars/ada.png", "ada.png"), ("top.txt", "top.txt"));
        importer.GetFileDownloadUrl("top.txt").Should().Be("https://storage.example.com/signed/top.txt");
        importer.SourceAuthToken.Should().BeNull();
    }

    [Fact]
    public async Task FirebaseImport_FilesAreOnlyListedWhenRequested()
    {
        var source = new FakeFirebaseSource { ObjectsFailure = new InvalidOperationException("must not be called") };

        var schema = await Schema(source);

        schema.Files.Should().BeEmpty();
        schema.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task FirebaseImport_StorageThatCannotBeListed_IsAnError()
    {
        var source = new FakeFirebaseSource { ObjectsFailure = new InvalidOperationException("no bucket") };

        var schema = await Schema(source, files: true);

        schema.Errors.Should().ContainSingle(e => e.StartsWith("files:") && e.Contains("no bucket"));
    }

    // ── Rule: records carry a stable source id, remapped references, and every value ──

    [Fact]
    public async Task FirebaseImport_Records_CarryTheirPathAndAStableIdThatReferencesResolveTo()
    {
        var source = Source("posts", Doc("posts/p1", new()
        {
            ["author"] = new FirebaseReference("users/u1"),
            ["when"] = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            ["where"] = new FirebaseGeoPoint(1, 2),
            ["gone"] = null,
            ["ratio"] = double.NaN
        }));
        source.Collections["users"] = [Doc("users/u1", new() { ["name"] = "Ada" })];
        var importer = Importer(source);
        await importer.FetchSchemaAsync(false, false, false);

        var post = (await importer.FetchRecordsAsync("posts", 1, 10)).Records.Single();
        var user = (await importer.FetchRecordsAsync("users", 1, 10)).Records.Single();

        post["firebase_path"]!.GetValue<string>().Should().Be("posts/p1");
        post["author"]!.GetValue<long>().Should().Be(user["id"]!.GetValue<long>());
        post["when"]!.GetValue<string>().Should().Be("2026-01-02T03:04:05.0000000Z");
        post["where"]!["latitude"]!.GetValue<double>().Should().Be(1);
        post.ContainsKey("gone").Should().BeFalse();
        post["firebase_extra"]!["ratio"]!.GetValue<string>().Should().Be("NaN");
        post["id"]!.GetValue<long>().Should().Be(FirebaseFieldMapping.StableId("posts/p1")).And.BePositive();
    }

    [Fact]
    public async Task FirebaseImport_RecordPaging_ContinuesAfterTheLastDocumentOfThePreviousPage()
    {
        var source = Source("items", Doc("items/a", new()), Doc("items/b", new()), Doc("items/c", new()));
        var importer = Importer(source);
        await importer.FetchSchemaAsync(false, false, false);

        var first = await importer.FetchRecordsAsync("items", 1, 2);
        var second = await importer.FetchRecordsAsync("items", 2, 2);

        first.Records.Should().HaveCount(2);
        second.Records.Single()["firebase_path"]!.GetValue<string>().Should().Be("items/c");
        source.Cursors.Should().Equal(null, "items/b");
    }

    [Fact]
    public async Task FirebaseImport_RecordsOfAnUnknownCollection_AreRefused()
    {
        var act = () => Importer(new FakeFirebaseSource()).FetchRecordsAsync("nope", 1, 10);

        await act.Should().ThrowAsync<ImportException>().WithMessage("*nope*");
    }

    // ── Rule: the import only ever reads from Firebase ──

    [Fact]
    public void FirebaseImport_NeverWritesToFirebase_TheSourceInterfaceOffersOnlyReads()
    {
        var verbs = typeof(IFirebaseSource).GetMethods().Where(m => !m.IsSpecialName).Select(m => m.Name).ToList();

        verbs.Should().OnlyContain(n => n.StartsWith("List") || n.StartsWith("Sample") || n.StartsWith("Read") || n.StartsWith("Count") || n == "SignDownloadUrl");
        GoogleFirebaseSource.ReadOnlyStorageScopes.Should().Equal("https://www.googleapis.com/auth/devstorage.read_only");
    }

    [Fact]
    public void FirebaseImport_DocumentPaths_AreRelativeToTheDatabase()
    {
        GoogleFirebaseSource.RelativePath("projects/p/databases/(default)/documents/users/u1").Should().Be("users/u1");
        GoogleFirebaseSource.RelativePath("users/u1").Should().Be("users/u1");
    }

    // ── Rule: applying twice creates nothing new ──

    private static StubHttpHandler EmptyOrders() => new StubHttpHandler()
        .On($"{Org}/entities", "[]")
        .On(HttpMethod.Post, $"{Org}/entities", """{"name":"orders","table_name":"orders","enable_rls":false,"is_system":false,"is_junction":false,"is_public":false,"lock_new_records":false,"fields":[],"id":1}""")
        .On(HttpMethod.Post, $"{Org}/entities/orders/fields", "{}")
        .On($"{Org}/entities/orders/items?page=1&pageSize=1", """{"items":[],"total_items":0}""")
        .On(HttpMethod.Post, $"{Org}/entities/orders/items", """{"id":1}""");

    private static ImportRunner Runner(FakeFirebaseSource source, StubHttpHandler target, bool dryRun = false) =>
        new(Importer(source), new AnythinkClient("99999", "https://api.example.com", new HttpClient(target)),
            new ImportOptions(DryRun: dryRun, IncludeFlows: false, IncludeData: true));

    [Fact]
    public async Task FirebaseImport_Apply_CreatesTheEntityAndOneRecordPerDocument()
    {
        var source = Source("orders", Doc("orders/o1", new() { ["total"] = 5L }), Doc("orders/o2", new() { ["total"] = 6L }));
        var target = EmptyOrders();

        var result = await Runner(source, target).RunAsync();

        result.Errors.Should().BeEmpty();
        result.EntitiesCreated.Should().Be(1);
        result.RecordsCreated.Should().Be(2);
        var created = target.Matching(HttpMethod.Post, "/orders/items").Select(r => JsonNode.Parse(r.Body)!["firebase_path"]!.GetValue<string>());
        created.Should().BeEquivalentTo("orders/o1", "orders/o2");
    }

    [Fact]
    public async Task FirebaseImport_RerunAfterApply_CreatesNothingNew()
    {
        var source = Source("orders", Doc("orders/o1", new() { ["total"] = 5L }));
        var existing = """{"name":"orders","table_name":"orders","enable_rls":false,"is_system":false,"is_junction":false,"is_public":false,"lock_new_records":false,"id":1,"fields":[{"id":1,"name":"firebase_path"},{"id":2,"name":"total"},{"id":3,"name":"firebase_extra"}]}""";
        var target = new StubHttpHandler()
            .On($"{Org}/entities", $"[{existing}]")
            .On($"{Org}/entities/orders/items?page=1&pageSize=1", """{"items":[{"id":1}],"total_items":1}""");

        var result = await Runner(source, target).RunAsync();

        target.Writes.Should().BeEmpty();
        result.RecordsCreated.Should().Be(0);
        result.Warnings.Should().Contain(w => w.Contains("orders") && w.Contains("already has 1 record"));
    }

    [Fact]
    public async Task FirebaseImport_DryRun_ChangesNothingAndLeavesTheUnmappedInThePlan()
    {
        var source = Source("orders", Doc("orders/o1", new() { ["total"] = 5L }, "items"));
        var target = new StubHttpHandler().On($"{Org}/entities", "[]");
        var runner = Runner(source, target, dryRun: true);

        var plan = await runner.BuildPlanAsync(await Importer(source).FetchSchemaAsync(false, false, false));

        target.Writes.Should().BeEmpty();
        plan.Items.Should().Contain(i => i.Kind == PlanKind.Report && i.Reason.StartsWith("orders/items:"));
        plan.Items.Should().Contain(i => i.Kind == PlanKind.Entity && i.Action == PlanAction.Create);
        plan.Source.Url.Should().Be("https://console.firebase.google.com/project/demo-project");
    }
}

public class FirebaseReviewRuleTests
{
    private const string Org = "/org/99999";

    private static FirebaseImporter Importer(FakeFirebaseSource source) => new(source, text => text);

    private static FakeFirebaseSource Source(string collection, params FirebaseDocument[] docs)
    {
        var source = new FakeFirebaseSource();
        source.Collections[collection] = [.. docs];
        return source;
    }

    private static FirebaseDocument Doc(string path, Dictionary<string, object?> fields) => FirebaseImporterTests.Doc(path, fields);

    // ── Rule: a value that does not fit its column goes to extra, never to a failed record ──

    [Fact]
    public async Task FirebaseImport_ValueThatDoesNotMatchTheSampledType_GoesToExtraNotAFailedRecord()
    {
        var source = Source("items", Doc("items/a", new() { ["n"] = 1L, ["s"] = "x", ["d"] = 1.5 }));
        var importer = Importer(source);
        await importer.FetchSchemaAsync(false, false, false);
        source.Collections["items"] =
        [
            Doc("items/b", new() { ["n"] = 5_000_000_000L, ["s"] = new Dictionary<string, object?> { ["k"] = 1L }, ["d"] = double.PositiveInfinity }),
            Doc("items/c", new() { ["n"] = 2.5, ["s"] = "ok", ["d"] = 2.5 })
        ];

        var records = (await importer.FetchRecordsAsync("items", 1, 10)).Records;

        var b = records.Single(r => r["firebase_path"]!.GetValue<string>() == "items/b");
        b.ContainsKey("n").Should().BeFalse();
        b["firebase_extra"]!["n"]!.GetValue<long>().Should().Be(5_000_000_000L);
        b["firebase_extra"]!["s"]!["k"]!.GetValue<long>().Should().Be(1);
        b["firebase_extra"]!["d"]!.GetValue<string>().Should().Be("Infinity");
        records.Single(r => r["firebase_path"]!.GetValue<string>() == "items/c")["firebase_extra"]!["n"]!.GetValue<double>().Should().Be(2.5);
        importer.LateWarnings.Should().Contain(w => w.StartsWith("items.n: 2 value(s) did not match"))
            .And.Contain(w => w.StartsWith("items.s: 1 value(s)")).And.Contain(w => w.StartsWith("items.d: 1 value(s)"));
    }

    [Fact]
    public async Task FirebaseImport_NonReferenceInAReferenceColumn_GoesToExtra()
    {
        var source = Source("posts", Doc("posts/a", new() { ["author"] = new FirebaseReference("users/u1") }));
        source.Collections["users"] = [Doc("users/u1", new())];
        var importer = Importer(source);
        await importer.FetchSchemaAsync(false, false, false);
        source.Collections["posts"] = [Doc("posts/b", new() { ["author"] = "just a name" })];

        var record = (await importer.FetchRecordsAsync("posts", 1, 10)).Records.Single();

        record.ContainsKey("author").Should().BeFalse();
        record["firebase_extra"]!["author"]!.GetValue<string>().Should().Be("just a name");
    }

    [Fact]
    public async Task FirebaseImport_MismatchWarnings_ReachTheRunnerResultAfterData()
    {
        var source = Source("orders", Doc("orders/a", new() { ["n"] = 1L }), Doc("orders/b", new() { ["n"] = "text" }));
        var importer = new FirebaseImporter(source, text => text, sampleSize: 1);
        var target = new StubHttpHandler()
            .On($"{Org}/entities", """[{"name":"orders","table_name":"orders","enable_rls":false,"is_system":false,"is_junction":false,"is_public":false,"lock_new_records":false,"fields":[],"id":1}]""")
            .On($"{Org}/entities/orders/items?page=1&pageSize=1", """{"items":[],"total_items":0}""")
            .On(HttpMethod.Post, $"{Org}/entities/orders/fields", "{}")
            .On(HttpMethod.Post, $"{Org}/entities/orders/items", """{"id":1}""");

        var result = await new ImportRunner(importer, new AnythinkClient("99999", "https://api.example.com", new HttpClient(target)),
            new ImportOptions(DryRun: false, IncludeFlows: false, IncludeData: true)).RunAsync();

        result.Warnings.Should().Contain(w => w.StartsWith("orders.n: 1 value(s) did not match"));
    }

    // ── Rule: names the API would reject never become entities or fields ──

    [Theory]
    [InlineData("9lives")]
    [InlineData("anythinkThing")]
    [InlineData("a_name_that_is_far_too_long_for_the_api_to_accept_as_a_column_name_ok")]
    public async Task FirebaseImport_FieldNamesTheApiRejects_GoToExtraWithAWarning(string field)
    {
        var source = Source("items", Doc("items/a", new() { [field] = "v", ["fine"] = "v" }));
        var importer = Importer(source);

        var schema = await importer.FetchSchemaAsync(false, false, false);
        var record = (await importer.FetchRecordsAsync("items", 1, 10)).Records.Single();

        schema.Collections.Single().Fields.Select(f => f.Name).Should().Contain("fine").And.OnlyContain(n => FirebaseFieldMapping.IsValidName(n));
        schema.Warnings.Should().Contain(w => w.Contains($"'{field}'") && w.Contains("firebase_extra"));
        record["firebase_extra"]![field]!.GetValue<string>().Should().Be("v");
    }

    [Theory]
    [InlineData("9lives", "firebase_9lives")]
    [InlineData("anythinkStuff", "firebase_anythink_stuff")]
    public async Task FirebaseImport_CollectionNamesTheApiRejects_GetAPrefixAndAWarning(string collection, string expected)
    {
        var schema = await Importer(Source(collection, Doc(collection + "/a", new()))).FetchSchemaAsync(false, false, false);

        schema.Collections.Single().Name.Should().Be(expected);
        schema.Warnings.Should().Contain(w => w.Contains($"'{collection}'") && w.Contains(expected));
    }

    [Fact]
    public async Task FirebaseImport_CollectionNamesThatCannotBeMadeValid_AreAPlanError()
    {
        var schema = await Importer(Source("!!!", Doc("!!!/a", new()))).FetchSchemaAsync(false, false, false);

        schema.Collections.Should().BeEmpty();
        schema.Errors.Should().ContainSingle(e => e.Contains("'!!!'") && e.Contains("not valid"));
    }

    // ── Rule: what is never read is listed once in the plan ──

    [Fact]
    public async Task FirebaseImport_NeverReadAreas_AreListedInOneWarning()
    {
        var schema = await Importer(new FakeFirebaseSource()).FetchSchemaAsync(false, false, false);

        var warning = schema.Warnings!.Single(w => w.Contains("Realtime Database"));
        warning.Should().Contain("named (non-default) Firestore databases").And.Contain("Cloud Functions")
            .And.Contain("Firestore indexes").And.Contain("Security Rules").And.Contain("others may exist");
    }

    // ── Rule: a reference that cannot be resolved keeps its path ──

    [Fact]
    public async Task FirebaseImport_SelfReference_KeepsTheOriginalPathInExtra()
    {
        var source = Source("categories", Doc("categories/child", new() { ["parent"] = new FirebaseReference("categories/root") }));
        var importer = Importer(source);
        await importer.FetchSchemaAsync(false, false, false);

        var record = (await importer.FetchRecordsAsync("categories", 1, 10)).Records.Single();

        record["parent"]!.GetValue<long>().Should().Be(FirebaseFieldMapping.StableId("categories/root"));
        record["firebase_extra"]!["parent"]!.GetValue<string>().Should().Be("categories/root");
    }

    // ── Rule: files ──

    [Fact]
    public void FirebaseImport_SignedLinks_LastFifteenMinutes()
    {
        var url = new GoogleFirebaseSource(FirebaseServiceAccountKey.Parse(GoogleFirebaseSourceTests.RealKeyJson()), "b").SignDownloadUrl("a.png");

        url.Should().Contain("X-Goog-Expires=900");
    }

    [Fact]
    public void FirebaseImport_DownloadLinkFailures_AreRedacted()
    {
        var key = FirebaseServiceAccountKey.Parse(FirebaseKeySafetyTests.KeyJson());
        var source = new FakeFirebaseSource { SignFailure = new InvalidOperationException("bad " + FirebaseKeySafetyTests.PrivateKey) };

        var act = () => new FirebaseImporter(source, key.Redact).GetFileDownloadUrl("a.png");

        act.Should().Throw<ImportException>().Which.Message.Should().Contain("[redacted]").And.NotContain("FAKE-KEY-MATERIAL");
    }

    // ── Rule: failed records are named by their Firebase path ──

    [Fact]
    public async Task FirebaseImport_FailedRecords_AreNamedByFirebasePath()
    {
        var source = Source("orders", Doc("orders/o1", new()));
        var target = new StubHttpHandler()
            .On($"{Org}/entities", """[{"name":"orders","table_name":"orders","enable_rls":false,"is_system":false,"is_junction":false,"is_public":false,"lock_new_records":false,"fields":[],"id":1}]""")
            .On($"{Org}/entities/orders/items?page=1&pageSize=1", """{"items":[],"total_items":0}""")
            .On(HttpMethod.Post, $"{Org}/entities/orders/fields", "{}")
            .On(HttpMethod.Post, $"{Org}/entities/orders/items", "no", System.Net.HttpStatusCode.BadRequest);

        var result = await new ImportRunner(Importer(source), new AnythinkClient("99999", "https://api.example.com", new HttpClient(target)),
            new ImportOptions(DryRun: false, IncludeFlows: false, IncludeData: true)).RunAsync();

        result.Errors.Should().Contain(e => e.StartsWith("orders#orders/o1:"));
    }

    // ── Rule: the shared reserved names and help are single sources ──

    [Fact]
    public void ReservedFieldNames_AreDefinedOnceForEveryImporter()
    {
        ReservedFieldNames.Names.Should().BeEquivalentTo("id", "tenant_id", "locked", "created_at", "updated_at");
    }

    [Fact]
    public void ImportFirebase_KeyHelp_RecommendsViewerOnlyRoles()
    {
        var help = typeof(Commands.ImportFirebaseSettings).GetProperty("Key")!
            .GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false).Cast<System.ComponentModel.DescriptionAttribute>().Single().Description;

        help.Should().Contain("Cloud Datastore Viewer").And.Contain("Storage Object Viewer").And.Contain("Firebase Authentication Viewer");
    }
}

public class GoogleFirebaseSourceTests
{
    internal static string RealKeyJson()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["type"] = "service_account",
            ["project_id"] = "demo-project",
            ["private_key_id"] = "kid",
            ["private_key"] = rsa.ExportPkcs8PrivateKeyPem(),
            ["client_email"] = "importer@demo-project.iam.gserviceaccount.com",
            ["token_uri"] = "https://oauth2.googleapis.com/token"
        });
    }

    // ── Rule: Firestore's own value types are turned into plain ones before any mapping happens ──

    [Fact]
    public void FirebaseImport_FirestoreValues_AreNormalisedToPlainTypes()
    {
        var value = new Dictionary<string, object>
        {
            ["when"] = Google.Cloud.Firestore.Timestamp.FromDateTimeOffset(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)),
            ["where"] = new Google.Cloud.Firestore.GeoPoint(1, 2),
            ["bytes"] = Google.Cloud.Firestore.Blob.CopyFrom([1, 2, 3]),
            ["list"] = new List<object> { "a", 2L },
            ["name"] = "Ada"
        };

        var map = (IReadOnlyDictionary<string, object?>)GoogleFirebaseSource.Normalise(value)!;

        map["when"].Should().Be(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        map["where"].Should().Be(new FirebaseGeoPoint(1, 2));
        map["bytes"].Should().BeEquivalentTo(new byte[] { 1, 2, 3 });
        ((List<object?>)map["list"]!).Should().Equal("a", 2L);
        map["name"].Should().Be("Ada");
    }

    // ── Rule: files are fetched through short-lived signed links, so no token or key travels with the download ──

    [Fact]
    public void FirebaseImport_FileDownloads_UseSignedLinksThatNeedNoToken()
    {
        var key = FirebaseServiceAccountKey.Parse(RealKeyJson());
        var url = new GoogleFirebaseSource(key, "my-bucket").SignDownloadUrl("avatars/ada.png");

        url.Should().StartWith("https://storage.googleapis.com/my-bucket/avatars/ada.png?").And.Contain("X-Goog-Signature=");
        url.Should().NotContain("PRIVATE").And.NotContain("access_token");
    }

    [Fact]
    public void FirebaseImport_SigningBeforeTheBucketIsKnown_IsRefused()
    {
        var key = FirebaseServiceAccountKey.Parse(FirebaseKeySafetyTests.KeyJson());

        var act = () => new GoogleFirebaseSource(key, null).SignDownloadUrl("a.png");

        act.Should().Throw<ImportException>();
    }
}
