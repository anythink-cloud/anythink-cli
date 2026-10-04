using AnythinkCli.Importers.Directus;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class DirectusImporterTests
{
    private const string Base = "https://cms.example.com";

    private static StubHttpHandler Directus(
        string collections = """{"data":[]}""",
        string fields      = """{"data":[]}""",
        string roles       = """{"data":[]}""",
        string policies    = """{"data":[]}""",
        string access      = """{"data":[]}""",
        string permissions = """{"data":[]}""",
        string flows       = """{"data":[]}""",
        string operations  = """{"data":[]}""") =>
        new StubHttpHandler()
            .On("/collections?limit=-1", collections)
            .On("/fields?limit=-1", fields)
            .On("/relations?limit=-1", """{"data":[]}""")
            .On("/roles?limit=-1", roles)
            .On("/policies?limit=-1", policies)
            .On("/access?limit=-1", access)
            .On("/permissions?limit=-1", permissions)
            .On("/flows?limit=-1", flows)
            .On("/operations?limit=-1", operations)
            .On("/files?limit=-1", """{"data":[]}""");

    private static DirectusImporter Importer(StubHttpHandler handler) =>
        new(Base, new DirectusClient(Base, "tok", new HttpClient(handler)));

    private static string Collection(string name) =>
        $$$"""{"collection":"{{{name}}}","meta":{"hidden":false},"schema":{"name":"{{{name}}}"}}""";

    private static string Pk(string collection, string type) =>
        $$$"""{"collection":"{{{collection}}}","field":"id","type":"{{{type}}}","meta":{},"schema":{"name":"id","is_primary_key":true}}""";

    private const string ReadAll = """[ "*" ]""";

    private static string Perm(string policy, string collection, string action = "read",
        string fields = ReadAll, string filter = "{}") =>
        $$"""{"id":1,"policy":"{{policy}}","collection":"{{collection}}","action":"{{action}}","permissions":{{filter}},"fields":{{fields}}}""";

    private static string Policy(string id, bool admin = false, bool app = false) =>
        $$"""{"id":"{{id}}","name":"{{id}}","admin_access":{{admin.ToString().ToLower()}},"app_access":{{app.ToString().ToLower()}}}""";

    private static string Access(string policy, string? role = null, string? user = null) =>
        $$"""{"role":{{(role is null ? "null" : $"\"{role}\"")}},"user":{{(user is null ? "null" : $"\"{user}\"")}},"policy":"{{policy}}"}""";

    // ── Rule: Directus lists are fetched in full, never truncated at the default page size ──

    [Fact]
    public async Task Schema_Fetch_Requests_Every_Row_Of_Collections_Fields_Relations_Flows_And_Operations()
    {
        var handler = Directus();

        await Importer(handler).FetchSchemaAsync(includeFlows: true, includeFiles: false, includeRoles: false);

        foreach (var path in new[] { "/collections", "/fields", "/relations", "/flows", "/operations" })
            handler.Requests.Should().Contain(r => r.PathAndQuery == path + "?limit=-1");
    }

    // ── Rule: Public means the access row with no role and no user — nothing else ──

    [Fact]
    public async Task ApiOnly_Policy_Attached_To_A_Role_Does_Not_Make_A_Collection_Public()
    {
        var handler = Directus(
            collections: $$"""{"data":[{{Collection("articles")}}]}""",
            roles:       """{"data":[{"id":"r1","name":"Service"}]}""",
            policies:    $$"""{"data":[{{Policy("api-only")}}]}""",
            access:      $$"""{"data":[{{Access("api-only", role: "r1")}}]}""",
            permissions: $$"""{"data":[{{Perm("api-only", "articles")}}]}""");

        var schema = await Importer(handler).FetchSchemaAsync(false, false, includeRoles: true);

        schema.Collections.Single().IsPublic.Should().BeFalse();
        schema.Roles.Single().CollectionPermissions.Should().Contain(("articles", "read"));
    }

    [Fact]
    public async Task Policy_Attached_With_No_Role_And_No_User_Makes_Readable_Collections_Public()
    {
        var handler = Directus(
            collections: $$"""{"data":[{{Collection("articles")}},{{Collection("secrets")}}]}""",
            policies:    $$"""{"data":[{{Policy("public")}}]}""",
            access:      $$"""{"data":[{{Access("public")}}]}""",
            permissions: $$"""{"data":[{{Perm("public", "articles")}}]}""");

        var schema = await Importer(handler).FetchSchemaAsync(false, false, includeRoles: true);

        schema.Collections.Single(c => c.Name == "articles").IsPublic.Should().BeTrue();
        schema.Collections.Single(c => c.Name == "secrets").IsPublic.Should().BeFalse();
    }

    [Fact]
    public async Task Collections_Default_To_Private_When_There_Is_No_Public_Attachment()
    {
        var handler = Directus(
            collections: $$"""{"data":[{{Collection("articles")}}]}""",
            policies:    $$"""{"data":[{{Policy("p1")}}]}""",
            permissions: $$"""{"data":[{{Perm("p1", "articles")}}]}""");

        var schema = await Importer(handler).FetchSchemaAsync(false, false, includeRoles: true);

        schema.Collections.Single().IsPublic.Should().BeFalse();
    }

    [Fact]
    public async Task User_Specific_Policy_Attachment_Is_Not_Treated_As_Public()
    {
        var handler = Directus(
            collections: $$"""{"data":[{{Collection("articles")}}]}""",
            policies:    $$"""{"data":[{{Policy("p1")}}]}""",
            access:      $$"""{"data":[{{Access("p1", user: "u1")}}]}""",
            permissions: $$"""{"data":[{{Perm("p1", "articles")}}]}""");

        var schema = await Importer(handler).FetchSchemaAsync(false, false, includeRoles: true);

        schema.Collections.Single().IsPublic.Should().BeFalse();
    }

    [Fact]
    public async Task Public_Attachment_Of_An_Admin_Policy_Never_Makes_Anything_Public()
    {
        var handler = Directus(
            collections: $$"""{"data":[{{Collection("articles")}}]}""",
            policies:    $$"""{"data":[{{Policy("root", admin: true)}}]}""",
            access:      $$"""{"data":[{{Access("root")}}]}""",
            permissions: $$"""{"data":[{{Perm("root", "articles")}}]}""");

        var schema = await Importer(handler).FetchSchemaAsync(false, false, includeRoles: true);

        schema.Collections.Single().IsPublic.Should().BeFalse();
    }

    // ── Rule: permissions that can't be mapped exactly are reported and never widened ──

    [Fact]
    public async Task Public_Read_With_A_Row_Filter_Is_Not_Made_Public_And_Is_Reported()
    {
        var handler = Directus(
            collections: $$"""{"data":[{{Collection("articles")}}]}""",
            policies:    $$"""{"data":[{{Policy("public")}}]}""",
            access:      $$"""{"data":[{{Access("public")}}]}""",
            permissions: $$"""{"data":[{{Perm("public", "articles", filter: """{"status":{"_eq":"published"}}""")}}]}""");

        var schema = await Importer(handler).FetchSchemaAsync(false, false, includeRoles: true);

        schema.Collections.Single().IsPublic.Should().BeFalse();
        schema.Warnings.Should().ContainSingle(w => w.Contains("articles") && w.Contains("not imported"));
    }

    [Fact]
    public async Task Role_Permission_With_Field_Limits_Is_Skipped_And_Reported()
    {
        var handler = Directus(
            collections: $$"""{"data":[{{Collection("articles")}}]}""",
            roles:       """{"data":[{"id":"r1","name":"Editor"}]}""",
            policies:    $$"""{"data":[{{Policy("p1", app: true)}}]}""",
            access:      $$"""{"data":[{{Access("p1", role: "r1")}}]}""",
            permissions: $$"""{"data":[{{Perm("p1", "articles", "read", fields: """["title"]""")}},{{Perm("p1", "articles", "update")}}]}""");

        var schema = await Importer(handler).FetchSchemaAsync(false, false, includeRoles: true);

        schema.Roles.Single().CollectionPermissions.Should().BeEquivalentTo(new[] { ("articles", "update") });
        schema.Warnings.Should().ContainSingle(w => w.Contains("read") && w.Contains("articles"));
    }

    // ── Rule: names that reach URLs must be valid entity names ──

    [Theory]
    [InlineData("My-Collection")]
    [InlineData("Articles")]
    [InlineData("a/../b")]
    [InlineData("posts?x=1")]
    public async Task Invalid_Collection_Names_Are_Skipped_And_Fail_The_Run(string name)
    {
        var handler = Directus(collections: $$"""{"data":[{{Collection(name)}},{{Collection("articles")}}]}""");

        var schema = await Importer(handler).FetchSchemaAsync(false, false, false);

        schema.Collections.Select(c => c.Name).Should().Equal("articles");
        schema.Errors.Should().ContainSingle(e => e.Contains(name));
    }

    [Fact]
    public async Task Fetching_Records_For_An_Invalid_Collection_Name_Is_Refused_Before_Any_Request()
    {
        var handler = Directus();

        var act = async () => await Importer(handler).FetchRecordsAsync("../users", 1, 100);

        await act.Should().ThrowAsync<AnythinkCli.Client.AnythinkException>();
        handler.Requests.Should().BeEmpty();
    }

    // ── Rule: only integer-keyed collections can have their data imported ──

    [Theory]
    [InlineData("uuid", true)]
    [InlineData("string", true)]
    [InlineData("integer", false)]
    [InlineData("bigInteger", false)]
    public async Task Non_Integer_Primary_Keys_Mark_Data_As_Unsupported(string pkType, bool unsupported)
    {
        var handler = Directus(
            collections: $$"""{"data":[{{Collection("articles")}}]}""",
            fields:      $$"""{"data":[{{Pk("articles", pkType)}}]}""");

        var schema = await Importer(handler).FetchSchemaAsync(false, false, false);

        (schema.Collections.Single().DataUnsupportedReason is not null).Should().Be(unsupported);
    }

    // ── Rule: credentials in the source URL are never printed ──

    [Theory]
    [InlineData("https://user:hunter2@cms.example.com/", "https://cms.example.com")]
    [InlineData("https://cms.example.com/?access_token=abc123", "https://cms.example.com")]
    [InlineData("https://cms.example.com:8055/directus/#frag", "https://cms.example.com:8055/directus")]
    public void ConnectionSummary_Redacts_Userinfo_Query_And_Fragment(string url, string expected)
    {
        var importer = new DirectusImporter(url, "tok");

        importer.ConnectionSummary.Should().Be(expected);
        importer.ConnectionSummary.Should().NotContain("hunter2").And.NotContain("abc123");
    }

    // ── Rule: flows ──

    [Fact]
    public async Task Item_Delete_Without_A_Plain_Key_Imports_As_A_Disabled_Placeholder()
    {
        var handler = Directus(
            flows: """{"data":[{"id":"f1","name":"cleanup","status":"active","trigger":"manual","operation":"o1"}]}""",
            operations: """{"data":[{"id":"o1","name":"Delete","key":"del","type":"item-delete","flow":"f1","options":{"collection":"articles","query":{"filter":{}}}}]}""");

        var schema = await Importer(handler).FetchSchemaAsync(true, false, false);

        var step = schema.Flows.Single().Steps.Single();
        step.Enabled.Should().BeFalse();
        step.Action.Should().NotBe("DeleteData");
        step.NeedsManualReview.Should().BeTrue();
    }

    [Fact]
    public async Task Role_With_No_Importable_Permissions_Is_Reported_Not_Silently_Skipped()
    {
        var handler = Directus(
            collections: $$"""{"data":[{{Collection("articles")}}]}""",
            roles:       """{"data":[{"id":"r1","name":"Editors [beta]"}]}""",
            policies:    $$"""{"data":[{{Policy("p1", app: true)}}]}""",
            access:      $$"""{"data":[{{Access("p1", role: "r1")}}]}""");

        var schema = await Importer(handler).FetchSchemaAsync(false, false, includeRoles: true);

        schema.Roles.Should().BeEmpty();
        schema.Warnings.Should().ContainSingle(w => w.Contains("Editors [beta]") && w.Contains("no importable permissions"));
    }

    [Fact]
    public async Task Request_Step_With_A_Bare_Secret_Reference_Imports_Neutered_And_Flagged()
    {
        var handler = Directus(
            flows: """{"data":[{"id":"f1","name":"call","status":"active","trigger":"manual","operation":"o1"}]}""",
            operations: """{"data":[{"id":"o1","name":"Call","key":"call","type":"request","flow":"f1","options":{"url":"https://a.example.com","method":"POST","body":"$anythink.secrets.OPENAI_KEY"}}]}""");

        var schema = await Importer(handler).FetchSchemaAsync(true, false, false);

        var step = schema.Flows.Single().Steps.Single();
        step.Parameters!.Value.GetRawText().Should().NotContain("$anythink");
        step.NeedsManualReview.Should().BeTrue();
    }
}
