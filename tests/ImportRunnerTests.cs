using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnythinkCli.Client;
using AnythinkCli.Commands;
using AnythinkCli.Importers;
using FluentAssertions;

namespace AnythinkCli.Tests;

internal sealed class FakeImporter(ImportSchema schema) : IPlatformImporter
{
    public string PlatformName => "Directus";
    public string ConnectionSummary => "https://cms.example.com";
    public string? SourceAuthToken => null;
    public string DownloadUrl { get; set; } = "http://127.0.0.1:1/unreachable";
    public List<JsonObject> Records { get; } = [];
    public int RecordFetches;
    public List<string> DownloadedIds { get; } = [];

    public Task<ImportSchema> FetchSchemaAsync(bool includeFlows, bool includeFiles, bool includeRoles) =>
        Task.FromResult(schema);

    public Task<ImportRecordPage> FetchRecordsAsync(string collectionName, int page, int pageSize)
    {
        RecordFetches++;
        return Task.FromResult(new ImportRecordPage(page == 1 ? Records : [], null));
    }

    public string GetFileDownloadUrl(string sourceFileId)
    {
        DownloadedIds.Add(sourceFileId);
        return DownloadUrl;
    }
}

public class ImportRunnerTests
{
    private const string Org = "/org/99999";

    private static AnythinkClient Client(StubHttpHandler h) =>
        new("99999", "https://api.example.com", new HttpClient(h));

    private static ImportSchema Schema(
        List<ImportCollection>? collections = null, List<ImportFlow>? flows = null,
        List<ImportFile>? files = null, List<ImportRole>? roles = null) =>
        new(collections ?? [], flows ?? [], files ?? [], roles ?? []);

    private static ImportCollection Articles(string? unsupported = null) =>
        new("articles", [], DataUnsupportedReason: unsupported);

    private const string EntityJson = """{"name":"articles","table_name":"articles","enable_rls":false,"is_system":false,"is_junction":false,"is_public":false,"lock_new_records":false,"fields":[],"id":1}""";

    private static StubHttpHandler TargetWithArticles() =>
        new StubHttpHandler().On($"{Org}/entities", $"[{EntityJson}]");

    private static async Task<ImportResult> Run(IPlatformImporter importer, StubHttpHandler target, ImportOptions options) =>
        await new ImportRunner(importer, Client(target), options).RunAsync();

    private static JsonObject Rec(object id) => new() { ["id"] = JsonSerializer.SerializeToNode(id), ["title"] = "x" };

    // ── Rule: dry run changes nothing ──

    [Fact]
    public async Task DryRun_Sends_No_Writes()
    {
        var target = TargetWithArticles();

        await Run(new FakeImporter(Schema([Articles(), new("pages", [])])), target, new ImportOptions(DryRun: true, IncludeFlows: false));

        target.Writes.Should().BeEmpty();
    }

    // ── Rule: existing roles are never modified ──

    private static List<ImportRole> EditorRole() =>
        [new("Editor", "From Directus", [("articles", "read"), ("articles", "update")])];

    [Fact]
    public async Task Existing_Role_Is_Left_Untouched_And_Reported()
    {
        var target = new StubHttpHandler()
            .On($"{Org}/entities", "[]")
            .On($"{Org}/roles", """[{"id":5,"name":"editor","description":"Mine","is_active":true,"permissions":[]}]""")
            .On($"{Org}/permissions", "[]");

        var result = await Run(new FakeImporter(Schema(roles: EditorRole())), target,
            new ImportOptions(DryRun: false, IncludeFlows: false, IncludeRoles: true));

        target.Writes.Should().BeEmpty("an existing role must never be updated, nor permissions created for it");
        result.RolesSkipped.Should().Be(1);
        result.RolesCreated.Should().Be(0);
        result.Warnings.Should().ContainSingle(w => w.Contains("Editor") && w.Contains("already exists"));
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task New_Role_Is_Created_With_Its_Permissions()
    {
        var target = new StubHttpHandler()
            .On($"{Org}/entities", "[]")
            .On($"{Org}/roles", "[]")
            .On($"{Org}/permissions", "[]")
            .On(HttpMethod.Post, $"{Org}/roles", """{"id":8,"name":"Editor","is_active":true}""")
            .On(HttpMethod.Post, $"{Org}/permissions", """{"id":31,"name":"articles:read","is_active":true}""")
            .On(HttpMethod.Put, $"{Org}/roles/8", """{"id":8,"name":"Editor","is_active":true}""");

        var result = await Run(new FakeImporter(Schema(roles: EditorRole())), target,
            new ImportOptions(DryRun: false, IncludeFlows: false, IncludeRoles: true));

        result.RolesCreated.Should().Be(1);
        target.Matching(HttpMethod.Put, "/roles/8").Should().ContainSingle();
        result.Errors.Should().BeEmpty();
    }

    // ── Rule: existing workflows are never modified ──

    private static ImportFlow Flow(params ImportStep[] steps) =>
        new("Nightly", [new("Manual", true, new { manual_entities = new[] { "orders" } })], steps.ToList());

    private static ImportStep Step(string id, string? next = null, bool start = false, bool enabled = true) =>
        new(id, $"key_{id}", $"Step {id}", "RunScript", start, "d",
            JsonSerializer.SerializeToElement(new { script = "run()" }), next, null, Enabled: enabled);

    [Fact]
    public async Task Existing_Workflow_With_The_Same_Name_Is_Left_Untouched_And_Reported()
    {
        var target = new StubHttpHandler()
            .On($"{Org}/entities", "[]")
            .On($"{Org}/workflows", """[{"id":3,"name":"nightly","trigger":"Manual","enabled":true,"steps":[]}]""");

        var result = await Run(new FakeImporter(Schema(flows: [Flow(Step("a", start: true))])), target,
            new ImportOptions(DryRun: false, IncludeFlows: true));

        target.Writes.Should().BeEmpty();
        result.WorkflowsSkipped.Should().Be(1);
        result.Warnings.Should().ContainSingle(w => w.Contains("Nightly") && w.Contains("already exists"));
    }

    [Fact]
    public async Task Flow_Whose_Trigger_Is_Missing_A_Required_Field_Is_Skipped_Not_Sent_To_The_Api()
    {
        var target = TargetForFlow();
        var incomplete = new ImportFlow("Nightly", [new("Manual", true, new { manual_entities = Array.Empty<string>() })],
            [Step("a", start: true)]);

        var result = await Run(new FakeImporter(Schema(flows: [incomplete])), target,
            new ImportOptions(DryRun: false, IncludeFlows: true));

        target.Writes.Should().BeEmpty();
        result.WorkflowsSkipped.Should().Be(1);
        result.Warnings.Should().ContainSingle(w => w.Contains("Nightly") && w.Contains("manual_entities"));
    }

    private static StubHttpHandler TargetForFlow() => new StubHttpHandler()
        .On($"{Org}/entities", "[]")
        .On($"{Org}/workflows", "[]")
        .On(HttpMethod.Post, $"{Org}/workflows", """{"id":40,"name":"Nightly","trigger":"Manual","enabled":false}""");

    [Fact]
    public async Task Step_Link_Wiring_Sends_The_Full_Step_Shape()
    {
        var target = TargetForFlow()
            .On(HttpMethod.Post, $"{Org}/workflows/40/steps", """{"id":1,"key":"k","name":"n","action":"RunScript","enabled":true,"is_start_step":true}""")
            .On(HttpMethod.Put, $"{Org}/workflows/40/steps/1", """{"id":1,"key":"k","name":"n","action":"RunScript","enabled":true,"is_start_step":true}""");

        var result = await Run(new FakeImporter(Schema(flows: [Flow(Step("a", next: "a", start: true))])), target,
            new ImportOptions(DryRun: false, IncludeFlows: true));

        var body = JsonNode.Parse(target.Matching(HttpMethod.Put, "/steps/1").Single().Body)!.AsObject();
        body["parameters"]!["script"]!.GetValue<string>().Should().Be("run()");
        body["is_start_step"]!.GetValue<bool>().Should().BeTrue();
        body["enabled"]!.GetValue<bool>().Should().BeTrue();
        body["on_success_step_id"]!.GetValue<int>().Should().Be(1);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Disabled_Steps_Are_Created_And_Re_Sent_Disabled()
    {
        var target = TargetForFlow()
            .On(HttpMethod.Post, $"{Org}/workflows/40/steps", """{"id":1,"key":"k","name":"n","action":"RunScript","enabled":false,"is_start_step":true}""")
            .On(HttpMethod.Put, $"{Org}/workflows/40/steps/1", """{"id":1,"key":"k","name":"n","action":"RunScript","enabled":false,"is_start_step":true}""");

        await Run(new FakeImporter(Schema(flows: [Flow(Step("a", next: "a", start: true, enabled: false))])), target,
            new ImportOptions(DryRun: false, IncludeFlows: true));

        JsonNode.Parse(target.Matching(HttpMethod.Post, "/steps").Single().Body)!["enabled"]!.GetValue<bool>().Should().BeFalse();
        JsonNode.Parse(target.Matching(HttpMethod.Put, "/steps/1").Single().Body)!["enabled"]!.GetValue<bool>().Should().BeFalse();
    }

    // ── Rule: every failure is recorded and fails the run ──

    [Fact]
    public async Task Failed_Step_Link_Is_Recorded_As_An_Error()
    {
        var target = TargetForFlow()
            .On(HttpMethod.Post, $"{Org}/workflows/40/steps", """{"id":1,"key":"k","name":"n","action":"RunScript","enabled":true,"is_start_step":true}""")
            .On(HttpMethod.Put, $"{Org}/workflows/40/steps/1", "boom", HttpStatusCode.InternalServerError);

        var result = await Run(new FakeImporter(Schema(flows: [Flow(Step("a", next: "a", start: true))])), target,
            new ImportOptions(DryRun: false, IncludeFlows: true));

        result.Errors.Should().ContainSingle(e => e.Contains("linking"));
        ImportDirectusCommand.ExitCodeFor(result).Should().Be(1);
    }

    [Fact]
    public async Task Link_To_A_Step_That_Failed_To_Create_Is_Recorded_As_An_Error()
    {
        var target = TargetForFlow()
            .On(HttpMethod.Post, $"{Org}/workflows/40/steps", """{"id":1,"key":"k","name":"n","action":"RunScript","enabled":true,"is_start_step":true}""")
            .On(HttpMethod.Put, $"{Org}/workflows/40/steps/1", """{"id":1,"key":"k","name":"n","action":"RunScript","enabled":true,"is_start_step":true}""");
        var flow = Flow(Step("a", next: "missing", start: true));

        var result = await Run(new FakeImporter(Schema(flows: [flow])), target,
            new ImportOptions(DryRun: false, IncludeFlows: true));

        result.Errors.Should().ContainSingle(e => e.Contains("missing") && e.Contains("not created"));
        ImportDirectusCommand.ExitCodeFor(result).Should().Be(1);
    }

    [Fact]
    public async Task Record_Without_An_Integer_Id_Is_Recorded_As_An_Error()
    {
        var target = TargetWithArticles().On($"{Org}/entities/articles/items?page=1&pageSize=1", """{"items":[],"total_items":0}""");
        var importer = new FakeImporter(Schema([Articles()])) { Records = { Rec("3f2a-uuid") } };

        var result = await Run(importer, target, new ImportOptions(DryRun: false, IncludeFlows: false, IncludeData: true));

        result.RecordsFailed.Should().Be(1);
        result.Errors.Should().ContainSingle(e => e.Contains("articles") && e.Contains("integer 'id'"));
        ImportDirectusCommand.ExitCodeFor(result).Should().Be(1);
    }

    [Fact]
    public async Task Failed_Count_Of_Existing_Records_Skips_The_Collection_And_Fails_The_Run()
    {
        var target = TargetWithArticles().On($"{Org}/entities/articles/items?page=1&pageSize=1", "boom", HttpStatusCode.InternalServerError);
        var importer = new FakeImporter(Schema([Articles()])) { Records = { Rec(1) } };

        var result = await Run(importer, target, new ImportOptions(DryRun: false, IncludeFlows: false, IncludeData: true));

        importer.RecordFetches.Should().Be(0);
        target.Matching(HttpMethod.Post, "/items").Should().BeEmpty();
        result.Errors.Should().ContainSingle(e => e.Contains("could not check"));
        ImportDirectusCommand.ExitCodeFor(result).Should().Be(1);
    }

    [Fact]
    public async Task Collection_With_Unsupported_Primary_Key_Is_Reported_Not_Imported()
    {
        var target = TargetWithArticles();
        var importer = new FakeImporter(Schema([Articles(unsupported: "primary key type 'uuid' is not supported")])) { Records = { Rec(1) } };

        var result = await Run(importer, target, new ImportOptions(DryRun: false, IncludeFlows: false, IncludeData: true));

        importer.RecordFetches.Should().Be(0);
        result.Errors.Should().ContainSingle(e => e.Contains("articles") && e.Contains("uuid"));
        ImportDirectusCommand.ExitCodeFor(result).Should().Be(1);
    }

    [Fact]
    public async Task Data_Is_Skipped_For_A_Collection_Whose_Entity_Failed_To_Create()
    {
        var target = new StubHttpHandler()
            .On($"{Org}/entities", "[]")
            .On(HttpMethod.Post, $"{Org}/entities", "boom", HttpStatusCode.InternalServerError);
        var importer = new FakeImporter(Schema([Articles()])) { Records = { Rec(1) } };

        var result = await Run(importer, target, new ImportOptions(DryRun: false, IncludeFlows: false, IncludeData: true));

        importer.RecordFetches.Should().Be(0);
        target.Matching(HttpMethod.Post, "/items").Should().BeEmpty();
        result.Errors.Should().ContainSingle(e => e.Contains("Create entity 'articles'"));
    }

    [Fact]
    public async Task Source_Reported_Errors_Fail_The_Run()
    {
        var schema = new ImportSchema([], [], [], [], null, ["collection 'X' skipped"]);

        var result = await Run(new FakeImporter(schema), new StubHttpHandler().On($"{Org}/entities", "[]"),
            new ImportOptions(DryRun: false, IncludeFlows: false));

        result.Errors.Should().Contain("collection 'X' skipped");
        ImportDirectusCommand.ExitCodeFor(result).Should().Be(1);
    }

    [Fact]
    public async Task Failed_File_Listing_Is_Recorded_And_No_File_Is_Downloaded()
    {
        var target = new StubHttpHandler()
            .On($"{Org}/entities", "[]")
            .On($"{Org}/files?page=1&pageSize=100", "boom", HttpStatusCode.InternalServerError);
        var importer = new FakeImporter(Schema(files: [new("src-1", "a.png", false)]));

        var result = await Run(importer, target, new ImportOptions(DryRun: false, IncludeFlows: false, IncludeFiles: true));

        importer.DownloadedIds.Should().BeEmpty();
        result.Errors.Should().ContainSingle(e => e.Contains("could not list existing"));
        ImportDirectusCommand.ExitCodeFor(result).Should().Be(1);
    }

    // ── Rule: source-supplied text is never parsed as console markup ──

    [Fact]
    public async Task Dry_Run_Prints_Warnings_Containing_Square_Brackets_Without_Crashing()
    {
        var schema = new ImportSchema([], [], [], [], ["policy 'Editors [beta]': read on 'a' [/] not imported."], null);

        var act = async () => await Run(new FakeImporter(schema), new StubHttpHandler().On($"{Org}/entities", "[]"),
            new ImportOptions(DryRun: true, IncludeFlows: false));

        await act.Should().NotThrowAsync();
    }

    // ── Rule: silently dropped data is reported ──

    private static ImportCollection WithFk() =>
        new("articles", [new("author", "integer", "input", "author", false, false, false, ForeignKeyCollection: "authors")]);

    [Fact]
    public async Task Dropped_References_Are_Reported_Per_Field_With_A_Count()
    {
        var target = new StubHttpHandler()
            .On($"{Org}/entities", $"[{EntityJson}]")
            .On($"{Org}/entities/articles/items?page=1&pageSize=1", """{"items":[],"total_items":0}""")
            .On(HttpMethod.Post, $"{Org}/entities/articles/items", """{"id":1}""");
        var importer = new FakeImporter(Schema([WithFk(), new("authors", [])]));
        importer.Records.Add(new JsonObject { ["id"] = 1, ["author"] = 9 });
        importer.Records.Add(new JsonObject { ["id"] = 2, ["author"] = 8 });

        var result = await Run(importer, target, new ImportOptions(DryRun: false, IncludeFlows: false, IncludeData: true));

        result.Warnings.Should().Contain(w => w.Contains("articles.author") && w.Contains("2 reference(s) dropped"));
    }

    [Fact]
    public async Task Skipping_A_Collection_That_Already_Has_Records_Is_A_Warning()
    {
        var target = TargetWithArticles().On($"{Org}/entities/articles/items?page=1&pageSize=1", """{"items":[{"id":1}],"total_items":4}""");

        var result = await Run(new FakeImporter(Schema([Articles()])), target,
            new ImportOptions(DryRun: false, IncludeFlows: false, IncludeData: true));

        result.Warnings.Should().ContainSingle(w => w.Contains("articles") && w.Contains("4 record(s)"));
    }

    [Fact]
    public async Task Created_Record_Whose_Response_Has_No_Id_Is_An_Error()
    {
        var target = TargetWithArticles()
            .On($"{Org}/entities/articles/items?page=1&pageSize=1", """{"items":[],"total_items":0}""")
            .On(HttpMethod.Post, $"{Org}/entities/articles/items", "{}");
        var importer = new FakeImporter(Schema([Articles()])) { Records = { Rec(1) } };

        var result = await Run(importer, target, new ImportOptions(DryRun: false, IncludeFlows: false, IncludeData: true));

        result.Errors.Should().ContainSingle(e => e.Contains("no id"));
    }

    // ── Rule: a failed listing is recorded and the run still summarises ──

    [Fact]
    public async Task Failed_Workflow_Listing_Is_Recorded_And_Does_Not_Abort_The_Run()
    {
        var target = new StubHttpHandler()
            .On($"{Org}/entities", "[]")
            .On($"{Org}/workflows", "boom", HttpStatusCode.InternalServerError);

        var result = await Run(new FakeImporter(Schema(flows: [Flow(Step("a", start: true))])), target,
            new ImportOptions(DryRun: false, IncludeFlows: true));

        result.Errors.Should().ContainSingle(e => e.Contains("could not list existing workflows"));
        target.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Failed_Role_Listing_Is_Recorded_And_Does_Not_Abort_The_Run()
    {
        var target = new StubHttpHandler()
            .On($"{Org}/entities", "[]")
            .On($"{Org}/roles", "boom", HttpStatusCode.InternalServerError);

        var result = await Run(new FakeImporter(Schema(roles: EditorRole())), target,
            new ImportOptions(DryRun: false, IncludeFlows: false, IncludeRoles: true));

        result.Errors.Should().ContainSingle(e => e.Contains("could not list existing roles"));
        target.Writes.Should().BeEmpty();
    }

    [Fact]
    public void Clean_Run_Exits_Zero()
    {
        ImportDirectusCommand.ExitCodeFor(new ImportResult(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, [])).Should().Be(0);
    }

    // ── Rule: files are matched by source identity, never by file name ──

    private const string FileJson = """{"id":{0},"original_file_name":"a.png","file_name":"x","file_type":"image/png","file_size":3,"is_public":false,"created_at":"2026-01-01T00:00:00Z"{1}}""";

    private static string File(int id, string? metadata = null) =>
        FileJson.Replace("{0}", id.ToString()).Replace("{1}",
            metadata is null ? "" : $",\"custom_metadata\":{JsonSerializer.Serialize(metadata)}");

    [Fact]
    public async Task Existing_File_With_The_Same_Name_But_No_Source_Id_Is_Not_Reused()
    {
        using var server = new FileServer([1, 2, 3]);
        var target = new StubHttpHandler()
            .On($"{Org}/entities", "[]")
            .On($"{Org}/files?page=1&pageSize=100", $$"""{"items":[{{File(7)}}],"total_items":1}""")
            .On(HttpMethod.Post, $"{Org}/files?isPublic=false", File(8))
            .On(HttpMethod.Put, $"{Org}/files/8", File(8));
        var importer = new FakeImporter(Schema(files: [new("src-1", "a.png", false)])) { DownloadUrl = server.Url };

        var result = await Run(importer, target, new ImportOptions(DryRun: false, IncludeFlows: false, IncludeFiles: true));

        result.FilesUploaded.Should().Be(1);
        result.FilesSkipped.Should().Be(0);
        target.Matching(HttpMethod.Put, "/files/8").Single().Body.Should().Contain("src-1").And.Contain("directus");
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Existing_File_Carrying_The_Source_Id_Is_Reused_Without_Uploading()
    {
        var meta = """{"imported_from":"directus","source_file_id":"src-1"}""";
        var target = new StubHttpHandler()
            .On($"{Org}/entities", "[]")
            .On($"{Org}/files?page=1&pageSize=100", $$"""{"items":[{{File(7, meta)}}],"total_items":1}""");
        var importer = new FakeImporter(Schema(files: [new("src-1", "renamed.png", false)]));

        var result = await Run(importer, target, new ImportOptions(DryRun: false, IncludeFlows: false, IncludeFiles: true));

        result.FilesSkipped.Should().Be(1);
        target.Writes.Should().BeEmpty();
        importer.DownloadedIds.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{"imported_from":"supabase","source_file_id":"src-1"}""")]
    [InlineData("not json")]
    [InlineData("""{"source_file_id":"src-1"}""")]
    [InlineData("")]
    public void Source_Id_Is_Only_Read_From_Metadata_Written_By_The_Same_Platform(string metadata)
    {
        ImportRunner.ReadSourceFileId(metadata, "Directus").Should().BeNull();
    }

    private sealed class FileServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        public string Url { get; }

        public FileServer(byte[] bytes)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}/f";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    try
                    {
                        var ctx = await _listener.GetContextAsync();
                        ctx.Response.OutputStream.Write(bytes);
                        ctx.Response.Close();
                    }
                    catch { break; }
                }
            });
        }

        public void Dispose() => _listener.Close();
    }
}
