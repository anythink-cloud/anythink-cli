using RichardSzalay.MockHttp;
using AnythinkCli.Commands;
using AnythinkCli.Models;
using FluentAssertions;
using System.Text.Json.Nodes;

namespace AnythinkCli.Tests;

// ── MigrateCommand.RemapHref ──────────────────────────────────────────────────

public class RemapHrefTests
{
    private const string Src = "11111111";
    private const string Dst = "22222222";

    [Fact]
    public void Swaps_OrgId_In_Entity_Href()
    {
        var result = MigrateCommand.RemapHref($"/org/{Src}/entities/categories", Src, Dst);
        result.Should().Be($"/org/{Dst}/entities/categories");
    }

    [Fact]
    public void Swaps_OrgId_In_Nested_Path()
    {
        var result = MigrateCommand.RemapHref($"/org/{Src}/entities/products/items/5", Src, Dst);
        result.Should().Be($"/org/{Dst}/entities/products/items/5");
    }

    [Fact]
    public void Returns_Href_Unchanged_When_OrgId_Not_Present()
    {
        const string href = "/dashboard/settings";
        MigrateCommand.RemapHref(href, Src, Dst).Should().Be(href);
    }

    [Fact]
    public void Returns_Empty_String_Unchanged()
    {
        MigrateCommand.RemapHref("", Src, Dst).Should().BeEmpty();
    }

    [Theory]
    [InlineData("/org/11111111/entities/foo", "/org/22222222/entities/foo")]
    [InlineData("/ORG/11111111/ENTITIES/foo", "/org/22222222/ENTITIES/foo")]
    public void Is_Case_Insensitive_On_Org_Segment(string input, string expected)
    {
        MigrateCommand.RemapHref(input, Src, Dst).Should().Be(expected);
    }

    [Fact]
    public void Does_Not_Corrupt_Href_When_Src_And_Dst_Are_Same()
    {
        var href = $"/org/{Src}/entities/orders";
        MigrateCommand.RemapHref(href, Src, Src).Should().Be(href);
    }
}

// ── MigrateCommand.StripEmbeddedFileRefs ─────────────────────────────────────

public class StripEmbeddedFileRefsTests
{
    // ── null / primitives pass through ────────────────────────────────────────

    [Fact]
    public void Null_Returns_Null()
        => MigrateCommand.StripEmbeddedFileRefs(null).Should().BeNull();

    [Fact]
    public void String_Value_Is_Unchanged()
    {
        var node = JsonValue.Create("hello");
        var result = MigrateCommand.StripEmbeddedFileRefs(node);
        result!.GetValue<string>().Should().Be("hello");
    }

    [Fact]
    public void Integer_Value_Is_Unchanged()
    {
        var node = JsonValue.Create(42);
        var result = MigrateCommand.StripEmbeddedFileRefs(node);
        result!.GetValue<int>().Should().Be(42);
    }

    // ── file objects are removed (return null) ────────────────────────────────

    [Theory]
    [InlineData("original_file_name")]
    [InlineData("file_name")]
    [InlineData("file_type")]
    public void Object_With_File_Key_Returns_Null(string fileKey)
    {
        var obj = new JsonObject { [fileKey] = "some-value", ["id"] = 5 };
        MigrateCommand.StripEmbeddedFileRefs(obj).Should().BeNull();
    }

    // ── safe objects are preserved ────────────────────────────────────────────

    [Fact]
    public void Object_Without_File_Keys_Is_Preserved()
    {
        var obj = new JsonObject { ["name"] = "Alice", ["score"] = 100 };
        var result = MigrateCommand.StripEmbeddedFileRefs(obj) as JsonObject;
        result.Should().NotBeNull();
        result!["name"]!.GetValue<string>().Should().Be("Alice");
        result["score"]!.GetValue<int>().Should().Be(100);
    }

    // ── arrays: file items are removed, others kept ───────────────────────────

    [Fact]
    public void Array_Removes_File_Objects_And_Keeps_Others()
    {
        var arr = new JsonArray
        {
            new JsonObject { ["original_file_name"] = "clip.mp3", ["id"] = 1 },
            new JsonObject { ["text"] = "Option A" },
            new JsonObject { ["file_name"] = "img.png" },
            new JsonObject { ["text"] = "Option B" },
        };

        var result = MigrateCommand.StripEmbeddedFileRefs(arr) as JsonArray;
        result.Should().NotBeNull();
        result!.Count.Should().Be(2);
        result[0]!["text"]!.GetValue<string>().Should().Be("Option A");
        result[1]!["text"]!.GetValue<string>().Should().Be("Option B");
    }

    [Fact]
    public void Array_Of_Primitives_Is_Unchanged()
    {
        var arr = new JsonArray { 1, 2, 3 };
        var result = MigrateCommand.StripEmbeddedFileRefs(arr) as JsonArray;
        result!.Count.Should().Be(3);
    }

    // ── nested structures ─────────────────────────────────────────────────────

    [Fact]
    public void Nested_File_Object_Inside_Options_Array_Is_Stripped()
    {
        // Mirrors the real mental_edge_questions#1 payload shape:
        // { "question_options": [ { "text": "Yes", "audio": { "file_name": "clip.mp3" } } ] }
        var payload = new JsonObject
        {
            ["question"] = "How do you feel?",
            ["question_options"] = new JsonArray
            {
                new JsonObject
                {
                    ["text"]  = "Yes",
                    ["audio"] = new JsonObject { ["file_name"] = "clip.mp3", ["id"] = 7 }
                },
                new JsonObject
                {
                    ["text"]  = "No",
                    ["audio"] = new JsonObject { ["file_name"] = "clip2.mp3", ["id"] = 8 }
                }
            }
        };

        var result = MigrateCommand.StripEmbeddedFileRefs(payload) as JsonObject;

        result!["question"]!.GetValue<string>().Should().Be("How do you feel?");
        var opts = result["question_options"] as JsonArray;
        opts!.Count.Should().Be(2);
        // audio sub-objects should have been stripped to null
        opts[0]!["audio"].Should().BeNull();
        opts[1]!["audio"].Should().BeNull();
        // text should still be there
        opts[0]!["text"]!.GetValue<string>().Should().Be("Yes");
    }

    [Fact]
    public void Deeply_Nested_File_Object_Is_Stripped()
    {
        var deep = new JsonObject
        {
            ["level1"] = new JsonObject
            {
                ["level2"] = new JsonObject
                {
                    ["file_type"] = "image/png",
                    ["id"] = 99
                }
            }
        };

        var result = MigrateCommand.StripEmbeddedFileRefs(deep) as JsonObject;
        var level1 = result!["level1"] as JsonObject;
        level1!["level2"].Should().BeNull();
    }
}

// ── MigrateCommand.CountItems ─────────────────────────────────────────────────

public class CountItemsTests
{
    private static MenuItemResponse Item(string name, params MenuItemResponse[] children) =>
        new(Id: 0, MenuId: 0, DisplayName: name, Icon: "", Href: "", ParentId: null, SortOrder: 0, Items: [.. children]);

    [Fact]
    public void Empty_List_Returns_Zero()
        => MigrateCommand.CountItems([]).Should().Be(0);

    [Fact]
    public void Flat_List_Returns_Count()
        => MigrateCommand.CountItems([Item("A"), Item("B"), Item("C")]).Should().Be(3);

    [Fact]
    public void Nested_Children_Are_Counted_Recursively()
    {
        var root = Item("Dashboard",
            Item("Overview"),
            Item("Reports", Item("Monthly"), Item("Annual")));

        MigrateCommand.CountItems([root]).Should().Be(5); // root + 4 descendants
    }
}

// ── migrate: fields, permissions, scope ──────────────────────────────────────

public class MigrateFieldsAndPermissionsTests
{
    private static Field AField(bool searchable, bool publiclySearchable, FieldOptionsRequest? options = null) =>
        new(1, "title", "Title", "varchar", "input", false, false, false, false,
            searchable, publiclySearchable, false, null, false, null, options);

    [Fact]
    public void Migrate_PublicField_IsMadeSearchable()
    {
        var request = MigrateCommand.BuildFieldRequest(AField(searchable: false, publiclySearchable: true), null);

        request.IsSearchable.Should().BeTrue();
        request.PubliclySearchable.Should().BeTrue();
    }

    [Fact]
    public void Migrate_PrivateUnsearchableField_StaysUnsearchable()
        => MigrateCommand.BuildFieldRequest(AField(false, false), null).IsSearchable.Should().BeFalse();

    [Fact]
    public void Migrate_SelectField_KeepsItsOptions()
    {
        var options = new FieldOptionsRequest(new SelectFieldOptions(
            [new SelectOption("One", System.Text.Json.Nodes.JsonValue.Create(1)), new SelectOption("Two", "two")], Multiple: true));

        var request = MigrateCommand.BuildFieldRequest(AField(false, false, options), null);

        System.Text.Json.JsonSerializer.Serialize(request.Options)
            .Should().Be("""{"select":{"options":[{"label":"One","value":1},{"label":"Two","value":"two"}],"multiple":true}}""");
    }

    [Fact]
    public async Task Migrate_Entity_GetsFullCrudPermissions()
    {
        var sent = new List<string>();
        var mock = new MockHttpMessageHandler();
        mock.ReplyWith(HttpMethod.Post, $"{CommandRunner.Org}/permissions", """{"id":9,"name":"x","is_active":true}""", sent);
        var existing = new List<Permission> { new(1, "albums:read", null, null, true), new(2, "ARTISTS:create", null, 5, true) };

        var added = await MigrateCommand.AddMissingCrudPermissionsAsync(
            CommandRunner.ClientFor(mock), ["albums", "artists"], existing, dryRun: false, []);

        added.Should().Be(6);
        sent.Select(b => JsonNode.Parse(b)!["name"]!.GetValue<string>()).Should().Equal(
            "albums:create", "albums:update", "albums:delete",
            "artists:read", "artists:update", "artists:delete");
    }

    [Fact]
    public async Task Migrate_DryRun_CountsMissingPermissionsWithoutCreatingThem()
    {
        var mock = new MockHttpMessageHandler();

        var added = await MigrateCommand.AddMissingCrudPermissionsAsync(
            CommandRunner.ClientFor(mock), ["albums"], [], dryRun: true, []);

        added.Should().Be(4);
    }

    [Fact]
    public async Task Migrate_PermissionTheApiRejects_IsReportedNotCounted()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{CommandRunner.Org}/permissions").Respond(System.Net.HttpStatusCode.BadRequest, "text/plain", "nope");
        var errors = new List<string>();

        var added = await MigrateCommand.AddMissingCrudPermissionsAsync(
            CommandRunner.ClientFor(mock), ["albums"], [new(1, "albums:read", null, null, true), new(2, "albums:update", null, null, true), new(3, "albums:delete", null, null, true)], dryRun: false, errors);

        added.Should().Be(0);
        errors.Should().ContainSingle().Which.Should().StartWith("Permission 'albums:create'");
    }

    [Fact]
    public async Task Migrate_PublicField_IsSentSearchableAndReported()
    {
        var sent = new List<string>();
        var mock = new MockHttpMessageHandler();
        mock.ReplyWith(HttpMethod.Post, $"{CommandRunner.Org}/entities/albums/fields", """{"id":1}""", sent);
        var madeSearchable = new List<string>();

        await MigrateCommand.MigrateField(CommandRunner.ClientFor(mock), "albums", AField(false, true), dryRun: false,
            [], [], [], madeSearchable, new(), new(), new());

        JsonNode.Parse(sent.Single())!["is_searchable"]!.GetValue<bool>().Should().BeTrue();
        madeSearchable.Should().Equal("albums.title");
    }

    [Fact]
    public void Migrate_NoIncludeFlags_NotInteractive_MigratesEntitiesOnly()
        => MigrateCommand.FlagScope(new MigrateSettings(), interactive: false).Should().Equal("Entities + Fields");

    [Fact]
    public void Migrate_NoIncludeFlags_Interactive_Asks()
        => MigrateCommand.FlagScope(new MigrateSettings(), interactive: true).Should().BeNull();

    [Fact]
    public void Migrate_ForceDataEntities_ImpliesData()
        => MigrateCommand.FlagScope(new MigrateSettings { ForceDataEntities = "albums" }, interactive: true)
            .Should().BeEquivalentTo(["Entities + Fields", "Data (Entity Records)"]);
}

public class BillingPlanTests
{
    [Fact]
    public void Plans_DecimalQuotas_Parse()
    {
        const string json = """
            {"id":"6f1c3c1e-0000-0000-0000-000000000001","name":"Starter","description":null,"resource":0,
             "monthly_price_cents":0,"annual_price_cents":0,"currency":"GBP","storage_quota_gb":0.5,
             "file_quota_gb":1.5,"user_quota":3,"custom_domain_enabled":false,"backups_enabled":false,
             "priority_support":false,"is_active":true,"display_order":1}
            """;

        var plan = System.Text.Json.JsonSerializer.Deserialize<BillingPlan>(json)!;

        $"{plan.StorageQuotaGb} {plan.FileQuotaGb}".Should().Be("0.5 1.5");
    }
}

[Collection("SequentialConfig")]
public class MigrateCommandTests : IDisposable
{
    private const string Host = "https://api.example.com";
    private const string SourceEntities = """
        [{"name":"albums","table_name":"my_albums","is_public":true,"id":5,"fields":[
          {"id":1,"name":"id","database_type":"integer","display_type":"input","locked":true},
          {"id":2,"name":"title","database_type":"varchar","display_type":"input","is_searchable":false,"publicly_searchable":true,"locked":false},
          {"id":3,"name":"format","database_type":"varchar","display_type":"select","locked":false,
           "options":{"select":{"options":[{"label":"Vinyl","value":1},{"label":"CD","value":"cd"}],"multiple":false}}}]}]
        """;

    private readonly string _config = Directory.CreateTempSubdirectory("anythink-cfg-").FullName;
    private readonly MockHttpMessageHandler _api = new();
    private readonly List<string> _entities = [], _fields = [], _permissions = [];

    public MigrateCommandTests()
    {
        AnythinkCli.Config.ConfigService.ConfigDirOverride = _config;
        AnythinkCli.Config.ConfigService.SaveProfile("src", new AnythinkCli.Config.Profile { OrgId = "1", ApiKey = "k", InstanceApiUrl = Host });
        AnythinkCli.Config.ConfigService.SaveProfile("dst", new AnythinkCli.Config.Profile { OrgId = "2", ApiKey = "k", InstanceApiUrl = Host });
        _api.ReplyWith(HttpMethod.Get, $"{Host}/org/1/entities", SourceEntities);
        _api.ReplyWith(HttpMethod.Get, $"{Host}/org/1/roles", "[]");
        _api.ReplyWith(HttpMethod.Get, $"{Host}/org/2/entities", "[]");
        _api.ReplyWith(HttpMethod.Get, $"{Host}/org/2/roles", "[]");
        _api.ReplyWith(HttpMethod.Get, $"{Host}/org/2/permissions", """[{"id":1,"name":"albums:read","entity_id":7,"is_active":true}]""");
        _api.ReplyWith(HttpMethod.Post, $"{Host}/org/2/entities", """{"name":"albums","table_name":"my_albums","id":7}""", _entities);
        _api.ReplyWith(HttpMethod.Post, $"{Host}/org/2/entities/albums/fields", """{"id":9,"name":"x"}""", _fields);
        _api.ReplyWith(HttpMethod.Post, $"{Host}/org/2/permissions", """{"id":9,"name":"x","is_active":true}""", _permissions);
    }

    public void Dispose()
    {
        AnythinkCli.Config.ConfigService.ConfigDirOverride = null;
        Directory.Delete(_config, true);
    }

    private Task<int> Migrate(bool dryRun) =>
        new MigrateCommand { ClientFactory = key => new AnythinkCli.Client.AnythinkClient(key == "src" ? "1" : "2", Host, new HttpClient(_api)) }
            .ExecuteAsync(null!, new MigrateSettings { From = "src", To = "dst", DryRun = dryRun });

    [Fact]
    public async Task Migrate_WithoutIncludeFlags_CopiesEntitiesWithSearchableFieldsOptionsAndFullPermissions()
    {
        (await Migrate(dryRun: false)).Should().Be(0);

        _entities.Should().ContainSingle();
        var fields = _fields.Select(b => JsonNode.Parse(b)!).ToDictionary(f => f["name"]!.GetValue<string>());
        fields.Keys.Should().BeEquivalentTo(["title", "format"]);
        fields["title"]["is_searchable"]!.GetValue<bool>().Should().BeTrue();
        fields["format"]["options"]!["select"]!["options"]!.AsArray().Should().HaveCount(2);
        _permissions.Select(b => JsonNode.Parse(b)!["name"]!.GetValue<string>())
            .Should().Equal("albums:create", "albums:update", "albums:delete");
    }

    [Fact]
    public async Task Migrate_DryRun_SendsNoWrites()
    {
        (await Migrate(dryRun: true)).Should().Be(0);

        _entities.Concat(_fields).Concat(_permissions).Should().BeEmpty();
    }
}
