using Anythink.ProjectImporters.Models;
using AnythinkCli.Importers;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnythinkCli.Client;
using Anythink.ProjectImporters;
using Anythink.ProjectImporters.Engine;
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
        new(new FakeImporter(schema), Client(target), options, new SpectreImportReporter());

    private static PlanContext Context(string url = "https://cms.example.com") => new(
        new PlanSource("Directus", UrlRedaction.Redact(url), ["schema"]),
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
        PlanJson.Serialise(ImportPlanner.Build(schema, snapshot, FullPlanScope, Context()))
            .Should().Be(PlanJson.Serialise(ImportPlanner.Build(schema, snapshot, FullPlanScope, Context())));
        plan.Items.Should().NotBeEmpty();
    }

    private static StubHttpHandler ApplyTarget() => MixedTarget()
        .On(HttpMethod.Post, $"{Org}/entities", PagesJson)
        .On(HttpMethod.Post, $"{Org}/entities/articles/fields", """{"id":2,"name":"body","database_type":"text","display_type":"input"}""")
        .On(HttpMethod.Post, $"{Org}/entities/pages/fields", """{"id":3,"name":"slug","database_type":"text","display_type":"input"}""")
        .On(HttpMethod.Post, $"{Org}/roles", """{"id":8,"name":"Writer","is_active":true}""")
        .On(HttpMethod.Post, $"{Org}/permissions", """{"id":31,"name":"articles:update","is_active":true}""")
        .On(HttpMethod.Put, $"{Org}/roles/8", """{"id":8,"name":"Writer","is_active":true}""")
        .On(HttpMethod.Post, $"{Org}/workflows", """{"id":40,"name":"Fresh","trigger":"Manual","enabled":false}""")
        .On(HttpMethod.Post, $"{Org}/workflows/40/steps", """{"id":1,"key":"k","name":"n","action":"RunScript","enabled":true,"is_start_step":true}""");

    // ── Rule: the runner needs no console, so a server can run it without a reporter ──

    [Fact]
    public async Task Runner_WithoutAReporter_AppliesTheSameChangesAsWithTheConsoleReporter()
    {
        var schema = MixedSchema();

        var withConsole = await Runner(schema, ApplyTarget(), FullScope).RunAsync();
        var silentTarget = ApplyTarget();
        var silent = await new ImportRunner(new FakeImporter(schema), Client(silentTarget), FullScope).RunAsync();

        silent.Should().BeEquivalentTo(withConsole);
        silentTarget.Writes.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Runner_WithoutAReporter_StillPlansADryRun()
    {
        var schema = MixedSchema();

        var text = await new ImportRunner(new FakeImporter(schema), Client(MixedTarget()), FullScope with { DryRun = true }).RunAsync();
        var json = await new ImportRunner(new FakeImporter(schema), Client(MixedTarget()), FullScope with { DryRun = true, PlanAsJson = true }).RunAsync();

        json.Should().BeEquivalentTo(text);
    }

    // ── Rule: a plan decides exactly what the runner then does for the same project ──

    [Fact]
    public async Task Plan_Decisions_Match_Runner_Behaviour_For_Same_Snapshot()
    {
        var schema = MixedSchema();
        var plan = await PlanFor(schema, MixedTarget());

        var applyTarget = ApplyTarget();
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
    public void Plan_Never_Contains_Source_Credentials()
    {
        var plan = ImportPlanner.Build(MixedSchema(), TargetSnapshot.Empty, FullPlanScope,
            Context("https://admin:hunter2@cms.example.com/dir?access_token=tok-123#frag"));

        var json = PlanJson.Serialise(plan);

        json.Should().NotContain("hunter2").And.NotContain("tok-123").And.NotContain("s3cret-step-parameter").And.NotContain("access_token");
    }

    [Theory]
    [InlineData("https://user:pw@cms.example.com/?token=abc", "https://cms.example.com")]
    [InlineData("https://cms.example.com:8055/base/?a=1#x", "https://cms.example.com:8055/base")]
    [InlineData("not a url", "(invalid url)")]
    public void Plan_Source_Url_Has_No_Userinfo_Or_Query(string raw, string expected)
    {
        UrlRedaction.Redact(raw).Should().Be(expected);
    }

    // ── Rule: a plan file is never silently replaced, and is private to its owner ──

    [Fact]
    public void Plan_File_Refuses_To_Overwrite_Without_Force()
    {
        var plan = ImportPlanner.Build(MixedSchema(), TargetSnapshot.Empty, FullPlanScope, Context());
        var path = Path.Combine(_dir, "plan.json");
        File.WriteAllText(path, "keep me");

        var act = () => PlanFile.Write(path, plan, force: false);

        act.Should().Throw<ImportException>().WithMessage("*--force*");
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

    // ── Rule: names match case-insensitively, as the runner and the API do ──

    private static TargetSnapshot WithArticles() =>
        TargetSnapshot.Empty with { Entities = [JsonSerializer.Deserialize<Entity>(ArticlesJson)!] };

    private static ImportPlan Plan(ImportSchema schema, TargetSnapshot? target = null) =>
        ImportPlanner.Build(schema, target ?? TargetSnapshot.Empty, FullPlanScope, Context());

    [Fact]
    public void Field_Differing_Only_By_Case_Is_Not_Created_Twice()
    {
        var plan = Plan(new ImportSchema([new("articles", [Field("Title")])], [], [], []), WithArticles());

        plan.Items.Should().NotContain(i => i.Kind == PlanKind.Field && i.Action == PlanAction.Create);
        plan.Items.Single(i => i.Kind == PlanKind.Entity).Action.Should().Be(PlanAction.Skip);
    }

    [Fact]
    public void Entity_Is_Matched_Case_Insensitively()
    {
        var plan = Plan(new ImportSchema([new("Articles", [Field("body")])], [], [], []), WithArticles());

        plan.Items.Single(i => i.Kind == PlanKind.Entity).Action.Should().Be(PlanAction.Merge);
    }

    [Fact]
    public void Each_Entity_Is_Merged_Skipped_Or_Created_On_Its_Own_Merits()
    {
        var plan = Plan(new ImportSchema(
            [new("articles", [Field("title")]), new("pages", [Field("slug")]), new("tags", [Field("name")])], [], [], []),
            WithArticles());

        plan.Items.Where(i => i.Kind == PlanKind.Entity).ToDictionary(i => i.TargetName!, i => i.Action)
            .Should().Equal(new Dictionary<string, PlanAction>
            {
                ["articles"] = PlanAction.Skip,
                ["pages"] = PlanAction.Create,
                ["tags"] = PlanAction.Create
            });
        Plan(new ImportSchema([new("articles", [Field("title"), Field("body")])], [], [], []), WithArticles())
            .Items.Single(i => i.Kind == PlanKind.Entity).Action.Should().Be(PlanAction.Merge);
    }

    [Fact]
    public async Task Two_New_Roles_Sharing_A_New_Permission_Create_It_Once()
    {
        var schema = new ImportSchema([], [], [], [new("A", null, [("posts", "read")]), new("B", null, [("posts", "read")])]);
        var plan = Plan(schema);

        plan.Items.Where(i => i.Kind == PlanKind.Permission).Select(i => i.Action)
            .Should().Equal(PlanAction.Create, PlanAction.Skip);

        var target = new StubHttpHandler()
            .On($"{Org}/entities", "[]").On($"{Org}/roles", "[]").On($"{Org}/permissions", "[]")
            .On(HttpMethod.Post, $"{Org}/roles", """{"id":8,"name":"A","is_active":true}""")
            .On(HttpMethod.Post, $"{Org}/permissions", """{"id":31,"name":"posts:read","is_active":true}""")
            .On(HttpMethod.Put, $"{Org}/roles/8", """{"id":8,"name":"A","is_active":true}""");
        await Runner(schema, target, new ImportOptions(false, false, IncludeRoles: true)).RunAsync();
        target.Matching(HttpMethod.Post, "/permissions").Should().ContainSingle();
    }

    // ── Rule: a refused plan file stops the command before anything is sent ──

    [Fact]
    public async Task Refusing_To_Overwrite_A_Plan_File_Sends_No_Requests()
    {
        var path = Path.Combine(_dir, "plan.json");
        File.WriteAllText(path, "keep me");
        var settings = new Commands.ImportDirectusSettings { Url = "https://cms.example.com", Plan = path };

        var act = () => Commands.ImportDirectusCommand.Preflight(settings);
        act.Should().Throw<ImportException>().WithMessage("*--force*");

        var target = MixedTarget();
        var run = () => Runner(MixedSchema(), target, FullScope with { DryRun = true, PlanPath = path }).RunAsync();
        await run.Should().ThrowAsync<ImportException>();
        target.Requests.Should().BeEmpty();
        File.ReadAllText(path).Should().Be("keep me");
    }

    [Fact]
    public void Force_Without_A_Plan_File_Is_Rejected()
    {
        var act = () => Commands.ImportDirectusCommand.Preflight(new Commands.ImportDirectusSettings { Url = "https://cms.example.com", Force = true });

        act.Should().Throw<CliException>().WithMessage("*--plan*");
    }

    [Fact]
    public void Plan_File_Is_Not_Written_Through_A_Symbolic_Link()
    {
        if (OperatingSystem.IsWindows()) return;
        var real = Path.Combine(_dir, "real.txt");
        File.WriteAllText(real, "precious");
        var link = Path.Combine(_dir, "link.json");
        File.CreateSymbolicLink(link, real);

        var act = () => PlanFile.Write(link, Plan(MixedSchema()), force: true);

        act.Should().Throw<ImportException>().WithMessage("*symbolic link*");
        File.ReadAllText(real).Should().Be("precious");
    }

    [Fact]
    public void A_Failed_Plan_Write_Leaves_No_Partial_File()
    {
        var path = Path.Combine(_dir, "missing-folder", "plan.json");

        var act = () => PlanFile.Write(path, Plan(MixedSchema()), force: false);

        act.Should().Throw<ImportException>();
        Directory.GetFileSystemEntries(_dir).Should().BeEmpty();
    }

    // ── Rule: the order the API lists things in never changes the fingerprint ──

    [Fact]
    public void Target_Hash_Ignores_The_Order_Of_Fields_Triggers_And_Steps()
    {
        var entity = JsonSerializer.Deserialize<Entity>(ArticlesJson)!;
        var second = entity.Fields![0] with { Id = 2, Name = "body" };
        var a = entity with { Fields = [entity.Fields![0], second] };
        var b = entity with { Fields = [second, entity.Fields![0]] };
        TargetHasher.Hash(a).Should().Be(TargetHasher.Hash(b));

        Workflow Wf(params string[] keys) => new(1, "w", null, null, true,
            keys.Select(k => new WorkflowStep(1, k, k, null, "RunScript", true, false, null, null)).ToList(),
            Triggers: keys.Select(k => new WorkflowTrigger(null, k)).ToList());
        TargetHasher.Hash(Wf("x", "y")).Should().Be(TargetHasher.Hash(Wf("y", "x")));
    }

    // ── Rule: a project that can't be read fails the plan with a clear message ──

    [Fact]
    public async Task Failed_Project_Read_Fails_The_Plan_With_A_Clear_Message()
    {
        var target = MixedTarget().On($"{Org}/workflows", "boom", HttpStatusCode.InternalServerError);

        var act = () => PlanFor(MixedSchema(), target);

        (await act.Should().ThrowAsync<ImportException>()).WithMessage("Couldn't read the project's workflows to plan the import:*");
    }

    // ── Rule: two source items with one name still get distinct plan keys ──

    [Fact]
    public void Duplicate_Source_Names_Get_Unique_Keys()
    {
        var schema = new ImportSchema([], [Flow("Nightly"), Flow("Nightly")], [],
            [new("Editor", null, []), new("Editor", null, [])]);

        var plan = Plan(schema);

        plan.Items.Select(i => i.Key).Should().OnlyHaveUniqueItems();
        plan.Items.Count(i => i.Kind == PlanKind.Workflow).Should().Be(2);
        plan.Items.Where(i => i.Kind == PlanKind.WorkflowStep).Select(i => i.Parent).Should().OnlyHaveUniqueItems();
    }

    // ── Rule: the diff groups fields under their entity and collapses what is already in place ──

    [Fact]
    public async Task Plan_Diff_Groups_Fields_And_Collapses_Unchanged_Items()
    {
        var plan = Plan(new ImportSchema([new("articles", [Field("title"), Field("body")])], [], [], [], ["careful"], null), WithArticles());

        var (_, output) = await CommandRunner.RunAsync(new MockHttpMessageHandler(), () =>
        {
            PlanDiffRenderer.Render(plan);
            return Task.FromResult(0);
        });

        var lines = output.Split('\n');
        lines.Should().ContainSingle(l => l.StartsWith("~ entity articles"));
        lines.Should().ContainSingle(l => l.StartsWith("    + field body"));
        output.Should().NotContain("field title");
        output.Should().Contain("Already in the project: 1 field(s)");
        lines.Should().ContainSingle(l => l.StartsWith("! review  careful"));
    }

    // ── Rule: a role's fingerprint is its permission set, not the order or ids the API lists them with ──

    [Fact]
    public void Target_Hash_Of_A_Role_Ignores_Permission_Order_But_Notices_A_Different_Set()
    {
        RoleResponse Role(params string[] permissions) => new(5, "editor", null, true,
            Permissions: permissions.Select((name, i) => new Permission(i + 1, name, null, null, true)).ToList());

        TargetHasher.Hash(Role("a:read", "b:read")).Should().Be(TargetHasher.Hash(Role("b:read", "a:read")));
        TargetHasher.Hash(Role("a:read", "b:read")).Should().NotBe(TargetHasher.Hash(Role("a:read")));
    }

    // ── Rule: the import command passes its plan flags through to the runner ──

    private static Commands.ImportDirectusCommand ImportCommand() =>
        new() { ImporterFactory = (_, _) => new FakeImporter(new ImportSchema([new("articles", [Field("title")])], [], [], [])) };

    // MockHttp rejects any request it has no stub for, so a write here would fail the command
    private static MockHttpMessageHandler EmptyProject()
    {
        var project = new MockHttpMessageHandler();
        project.ReplyWith(HttpMethod.Get, $"{CommandRunner.Org}/entities", "[]");
        return project;
    }

    [Fact]
    public async Task Plan_Flag_Saves_The_Plan_And_Changes_Nothing()
    {
        var path = Path.Combine(_dir, "plan.json");

        var (code, output) = await CommandRunner.RunAsync(EmptyProject(), () => ImportCommand().ExecuteAsync(null!,
            new Commands.ImportDirectusSettings { Url = "https://cms.example.com", Token = "t", Plan = path }));

        code.Should().Be(0, output);
        PlanJson.Deserialise(File.ReadAllText(path)).Items.Should().Contain(i => i.Kind == PlanKind.Entity && i.Action == PlanAction.Create);
        output.Should().Contain("Nothing in the project was changed");
    }

    [Fact]
    public async Task Json_With_Dry_Run_Prints_Only_The_Plan_As_Json()
    {
        var (code, output) = await CommandRunner.RunAsync(EmptyProject(), () => ImportCommand().ExecuteAsync(null!,
            new Commands.ImportDirectusSettings { Url = "https://cms.example.com", Token = "t", DryRun = true, Json = true }));

        code.Should().Be(0, output);
        JsonNode.Parse(output)!["items"]!.AsArray().Should().NotBeEmpty();
        output.Should().NotContain("Directus:").And.NotContain("Reading the project");
    }
}
