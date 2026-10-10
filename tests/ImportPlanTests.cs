using System.Text.Json;
using System.Text.Json.Nodes;
using AnythinkCli.Client;
using AnythinkCli.Importers;
using AnythinkCli.Importers.Engine;
using AnythinkCli.Models;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class ImportPlanTests : IDisposable
{
    private const string Org = "/org/99999";
    private readonly string _dir = Directory.CreateTempSubdirectory("import-plan-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static AnythinkClient Client(StubHttpHandler h) =>
        new("99999", "https://api.example.com", new HttpClient(h));

    private static ImportFieldSpec Field(string name, string? fk = null) =>
        new(name, "text", "input", name, false, false, false, ForeignKeyCollection: fk);

    private static ImportStep Step(string id, bool review = false) =>
        new(id, $"key_{id}", $"Step {id}", "RunScript", id == "a", null,
            JsonSerializer.SerializeToElement(new { script = "run()", auth = "s3cret-step-parameter" }), null, null,
            NeedsManualReview: review, ReviewNote: review ? "check the script" : null);

    private static ImportFlow Flow(string name, bool valid = true) =>
        new(name, [new("Manual", true, new { manual_entities = valid ? new[] { "orders" } : Array.Empty<string>() })], [Step("a", review: true)]);

    private const string ArticlesJson =
        """{"name":"articles","table_name":"articles","enable_rls":false,"is_system":false,"is_junction":false,"is_public":false,"lock_new_records":false,"id":1,"fields":[{"id":1,"name":"title","label":"Title","database_type":"text","display_type":"input","is_required":false,"is_name_field":false,"is_unique":false,"is_immutable":false,"is_searchable":false,"publicly_searchable":false,"is_indexed":false,"default_value":null,"locked":false}]}""";

    private const string PagesJson =
        """{"name":"pages","table_name":"pages","enable_rls":false,"is_system":false,"is_junction":false,"is_public":false,"lock_new_records":false,"id":2,"fields":[]}""";

    private static string FileJson(int id, string meta) =>
        $$"""{"id":{{id}},"original_file_name":"a.png","file_name":"x","file_type":"image/png","file_size":3,"is_public":false,"created_at":"2026-01-01T00:00:00Z","custom_metadata":{{JsonSerializer.Serialize(meta)}}}""";

    private static ImportSchema MixedSchema() => new(
        [
            new("articles", [Field("title"), Field("body")]),
            new("pages", [Field("slug"), Field("author", fk: "articles")], DataUnsupportedReason: "primary key type 'uuid' is not supported")
        ],
        [Flow("Nightly"), Flow("Broken", valid: false), Flow("Fresh")],
        [new("src-1", "a.png", false)],
        [
            new("Editor", null, [("articles", "read")]),
            new("Writer", "Writes", [("articles", "read"), ("articles", "update")]),
            new("Admin User", null, [("articles", "read")])
        ],
        ["policy 'Editors' read on 'x' not imported."],
        ["collection 'Legacy' skipped"]);

    private static StubHttpHandler MixedTarget() => new StubHttpHandler()
        .On($"{Org}/entities", $"[{ArticlesJson}]")
        .On($"{Org}/roles", """[{"id":5,"name":"editor","is_active":true,"permissions":[]}]""")
        .On($"{Org}/permissions", """[{"id":9,"name":"articles:read","is_active":true}]""")
        .On($"{Org}/workflows", """[{"id":3,"name":"nightly","trigger":"Manual","enabled":true,"steps":[]}]""")
        .On($"{Org}/files?page=1&pageSize=100",
            $$"""{"items":[{{FileJson(7, """{"imported_from":"directus","source_file_id":"src-1"}""")}}],"total_items":1}""")
        .On($"{Org}/entities/articles/items?page=1&pageSize=1", """{"items":[],"total_items":3}""");

    private static readonly ImportOptions FullScope = new(DryRun: false, IncludeFlows: true, IncludeData: true, IncludeFiles: true, IncludeRoles: true);
    private static readonly PlanOptions FullPlanScope = new(true, true, true, true);

    private static ImportRunner Runner(ImportSchema schema, StubHttpHandler target, ImportOptions options) =>
        new(new FakeImporter(schema), Client(target), options);

    private static PlanContext Context(string url = "https://cms.example.com") => new(
        new PlanSource("Directus", PlanSource.SanitiseUrl(url), ["schema"]),
        new PlanTarget("https://api.example.com", "99999"), "run-1", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));

    private static async Task<ImportPlan> PlanFor(ImportSchema schema, StubHttpHandler target) =>
        await Runner(schema, target, FullScope with { DryRun = true }).BuildPlanAsync(schema);

    // ── Rule: planning reads the project and never writes to it ──

    [Fact]
    public async Task Plan_Is_Pure_And_Sends_Only_Gets()
    {
        var target = MixedTarget();
        var schema = MixedSchema();

        var plan = await PlanFor(schema, target);

        target.Requests.Should().NotBeEmpty().And.OnlyContain(r => r.Method == HttpMethod.Get);

        var snapshot = await TargetSnapshotReader.ReadAsync(Client(MixedTarget()), FullPlanScope, "Directus", schema);
        var again = ImportPlanner.Build(schema, snapshot, FullPlanScope, Context());
        PlanJson.Serialise(ImportPlanner.Build(schema, snapshot, FullPlanScope, Context())).Should().Be(PlanJson.Serialise(again));
        plan.Items.Should().NotBeEmpty();
    }

    // ── Rule: a plan decides exactly what the runner then does for the same project ──

    [Fact]
    public async Task Plan_Decisions_Match_Runner_Behaviour_For_Same_Snapshot()
    {
        var schema = MixedSchema();
        var plan = await PlanFor(schema, MixedTarget());

        var applyTarget = MixedTarget()
            .On(HttpMethod.Post, $"{Org}/entities", PagesJson)
            .On(HttpMethod.Post, $"{Org}/entities/articles/fields", """{"id":2,"name":"body","database_type":"text","display_type":"input"}""")
            .On(HttpMethod.Post, $"{Org}/entities/pages/fields", """{"id":3,"name":"slug","database_type":"text","display_type":"input"}""")
            .On(HttpMethod.Post, $"{Org}/roles", """{"id":8,"name":"Writer","is_active":true}""")
            .On(HttpMethod.Post, $"{Org}/permissions", """{"id":31,"name":"articles:update","is_active":true}""")
            .On(HttpMethod.Put, $"{Org}/roles/8", """{"id":8,"name":"Writer","is_active":true}""")
            .On(HttpMethod.Post, $"{Org}/workflows", """{"id":40,"name":"Fresh","trigger":"Manual","enabled":false}""")
            .On(HttpMethod.Post, $"{Org}/workflows/40/steps", """{"id":1,"key":"k","name":"n","action":"RunScript","enabled":true,"is_start_step":true}""");
        var result = await Runner(schema, applyTarget, FullScope).RunAsync();

        int Count(string kind, PlanAction action) => plan.Items.Count(i => i.Kind == kind && i.Action == action);

        Count(PlanKind.Entity, PlanAction.Create).Should().Be(result.EntitiesCreated);
        (Count(PlanKind.Entity, PlanAction.Skip) + Count(PlanKind.Entity, PlanAction.Merge)).Should().Be(result.EntitiesSkipped);
        Count(PlanKind.Field, PlanAction.Create).Should().Be(result.FieldsCreated);
        Count(PlanKind.Field, PlanAction.Skip).Should().Be(result.FieldsSkipped);
        Count(PlanKind.Workflow, PlanAction.Create).Should().Be(result.WorkflowsCreated);
        Count(PlanKind.Workflow, PlanAction.Skip).Should().Be(result.WorkflowsSkipped);
        Count(PlanKind.WorkflowStep, PlanAction.Create).Should().Be(result.WorkflowStepsCreated);
        Count(PlanKind.Role, PlanAction.Create).Should().Be(result.RolesCreated);
        Count(PlanKind.Role, PlanAction.Skip).Should().Be(result.RolesSkipped);
        Count(PlanKind.File, PlanAction.Skip).Should().Be(result.FilesSkipped);
        Count(PlanKind.File, PlanAction.Create).Should().Be(result.FilesUploaded);
        plan.Items.Count(i => i.Kind == PlanKind.Permission).Should().Be(result.PermissionsAttached);
        Count(PlanKind.Permission, PlanAction.Create).Should().Be(applyTarget.Matching(HttpMethod.Post, "/permissions").Count());

        var skipReasons = plan.Items.Where(i => i.Action == PlanAction.Skip && i.Category != ReportCategory.Mapped || i is { Kind: PlanKind.Role or PlanKind.Workflow, Action: PlanAction.Skip })
            .Select(i => i.Reason).Concat(schema.Warnings!);
        result.Warnings.Should().BeEquivalentTo(skipReasons);
        result.Errors.Should().BeEquivalentTo(plan.Items.Where(i => i.Category == ReportCategory.NotSupported).Select(i => i.Reason));
    }

    // ── Rule: the plan file is snake_case JSON that reads back as the same plan ──

    [Fact]
    public async Task Plan_File_Round_Trips_In_Snake_Case()
    {
        var plan = await PlanFor(MixedSchema(), MixedTarget());

        var json = PlanJson.Serialise(plan);
        var root = JsonNode.Parse(json)!.AsObject();

        root.Should().ContainKeys("format_version", "run_id", "created_at", "target_fingerprint", "items");
        root["format_version"]!.GetValue<int>().Should().Be(1);
        root["items"]![0]!.AsObject().Should().ContainKeys("key", "kind", "action", "category", "reason");
        json.Should().Contain("\"mapped_with_changes\"").And.Contain("\"needs_review\"").And.Contain("\"not_supported\"");
        json.Should().NotMatchRegex("\"[a-z]+[A-Z]");

        var back = PlanJson.Deserialise(json);
        back.Items.Select(i => (i.Key, i.Kind, i.Action, i.Category, i.Reason, i.TargetHash))
            .Should().Equal(plan.Items.Select(i => (i.Key, i.Kind, i.Action, i.Category, i.Reason, i.TargetHash)));
        PlanJson.Serialise(back).Should().Be(json);
    }

    // ── Rule: the same input always yields the same item order, whatever order the source lists things ──

    [Fact]
    public void Plan_Items_Are_Deterministically_Ordered()
    {
        var schema = MixedSchema();
        var reversed = schema with
        {
            Collections = schema.Collections.AsEnumerable().Reverse().ToList(),
            Flows = schema.Flows.AsEnumerable().Reverse().ToList(),
            Roles = schema.Roles.AsEnumerable().Reverse().ToList()
        };

        var a = ImportPlanner.Build(schema, TargetSnapshot.Empty, FullPlanScope, Context());
        var b = ImportPlanner.Build(reversed, TargetSnapshot.Empty, FullPlanScope, Context());

        a.Items.Select(i => i.Key).Should().Equal(b.Items.Select(i => i.Key));
        a.Items.Select(i => i.Kind).Should().BeInAscendingOrder(Comparer<string>.Create((x, y) => PlanKind.Rank(x).CompareTo(PlanKind.Rank(y))));
        a.Items.Take(1).Single().Kind.Should().Be(PlanKind.Entity);
    }

    // ── Rule: no credential from the source or its flows reaches a plan ──

    [Fact]
    public async Task Plan_Never_Contains_Source_Credentials()
    {
        var importer = new FakeImporter(MixedSchema());
        var schema = MixedSchema();
        var plan = ImportPlanner.Build(schema, TargetSnapshot.Empty, FullPlanScope,
            Context("https://admin:hunter2@cms.example.com/dir?access_token=tok-123#frag"));

        var json = PlanJson.Serialise(plan);

        json.Should().NotContain("hunter2").And.NotContain("tok-123").And.NotContain("s3cret-step-parameter").And.NotContain("access_token");
        importer.SourceAuthToken.Should().BeNull();
        await Task.CompletedTask;
    }

    [Theory]
    [InlineData("https://user:pw@cms.example.com/?token=abc", "https://cms.example.com/")]
    [InlineData("https://cms.example.com:8055/base/?a=1#x", "https://cms.example.com:8055/base/")]
    [InlineData("not a url", "")]
    public void Plan_Source_Url_Has_No_Userinfo_Or_Query(string raw, string expected)
    {
        PlanSource.SanitiseUrl(raw).Should().Be(expected);
    }

    // ── Rule: a plan file is never silently replaced, and is private to its owner ──

    [Fact]
    public void Plan_File_Refuses_To_Overwrite_Without_Force()
    {
        var plan = ImportPlanner.Build(MixedSchema(), TargetSnapshot.Empty, FullPlanScope, Context());
        var path = Path.Combine(_dir, "plan.json");
        File.WriteAllText(path, "keep me");

        var act = () => PlanFile.Write(path, plan, force: false);

        act.Should().Throw<CliException>().WithMessage("*--force*");
        File.ReadAllText(path).Should().Be("keep me");

        PlanFile.Write(path, plan, force: true);
        PlanJson.Deserialise(File.ReadAllText(path)).RunId.Should().Be("run-1");
        if (!OperatingSystem.IsWindows())
            File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    // ── Rule: --dry-run is the plan without the file ──

    [Fact]
    public async Task Dry_Run_Is_Plan_Without_File()
    {
        var schema = MixedSchema();
        var path = Path.Combine(_dir, "out.json");
        var dry = MixedTarget();
        var planned = MixedTarget();

        var dryResult = await Runner(schema, dry, FullScope with { DryRun = true }).RunAsync();
        var planResult = await Runner(schema, planned, FullScope with { DryRun = true, PlanPath = path }).RunAsync();

        File.Exists(path).Should().BeTrue();
        Directory.GetFiles(_dir).Should().ContainSingle();
        dry.Writes.Should().BeEmpty();
        planned.Writes.Should().BeEmpty();
        dryResult.Errors.Should().Equal(planResult.Errors);
        dryResult.Warnings.Should().Equal(planResult.Warnings);

        var saved = PlanJson.Deserialise(File.ReadAllText(path));
        saved.Items.Select(i => i.Key).Should().Equal((await PlanFor(schema, MixedTarget())).Items.Select(i => i.Key));
    }

    // ── Rule: what the source reports as a warning or error stays visible in the plan ──

    [Fact]
    public void Schema_Warnings_Become_Report_Items()
    {
        var plan = ImportPlanner.Build(MixedSchema(), TargetSnapshot.Empty, FullPlanScope, Context());

        plan.Items.Should().Contain(i => i.Kind == PlanKind.Report && i.Action == PlanAction.Report
            && i.Category == ReportCategory.NeedsReview && i.Reason.Contains("Editors"));
        plan.Items.Should().Contain(i => i.Kind == PlanKind.Report && i.Action == PlanAction.Report
            && i.Category == ReportCategory.NotSupported && i.Reason.Contains("Legacy"));
    }

    // ── Rule: names from the source are never parsed as console markup ──

    [Fact]
    public async Task Plan_Diff_Escapes_Source_Names()
    {
        var schema = new ImportSchema([new("a[/]b[red]", [Field("x[bold]")])], [], [], [],
            ["policy 'Editors [beta]' [/] not imported."], null);
        var plan = ImportPlanner.Build(schema, TargetSnapshot.Empty, new PlanOptions(false, false, false, false), Context());

        var (_, output) = await CommandRunner.RunAsync(new MockHttpMessageHandler(), () =>
        {
            PlanDiffRenderer.Render(plan);
            return Task.FromResult(0);
        });

        output.Should().Contain("a[/]b[red]").And.Contain("x[bold]").And.Contain("Editors [beta]");
    }
}
