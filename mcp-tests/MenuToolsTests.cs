using System.Net;
using System.Text.Json;
using AnythinkCli.Client;
using AnythinkMcp.Cli;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

public class MenuToolsTests
{
    private const string Org = "https://api.my.anythink.cloud/org/42";

    // Menu 92: item 299 (with child 301), item 300, and a locked built-in item 10.
    private const string Menu92 = """
        {"id":92,"name":"Admin","items":[
          {"id":10,"menu_id":92,"display_name":"Dashboard","icon":"Home","href":"/org/42","parent_id":null,"sort_order":0,"locked":true,"items":[]},
          {"id":299,"menu_id":92,"display_name":"Badges","icon":"Award","href":"/org/42/entities/badges","parent_id":null,"sort_order":1,"locked":false,"items":[
            {"id":301,"menu_id":92,"display_name":"Levels","icon":"Layers","href":"/org/42/entities/levels","parent_id":299,"sort_order":1,"locked":false,"items":[]}]},
          {"id":300,"menu_id":92,"display_name":"[draft] Posts","icon":"FileText","href":"/org/42/entities/posts","parent_id":null,"sort_order":2,"locked":false,"items":[]}
        ]}
        """;

    private const string MenuWithGrandchild = """
        {"id":92,"name":"Admin","items":[
          {"id":299,"menu_id":92,"display_name":"Badges","icon":"Award","href":"/b","parent_id":null,"sort_order":1,"locked":false,"items":[
            {"id":301,"menu_id":92,"display_name":"Levels","icon":"Layers","href":"/l","parent_id":299,"sort_order":1,"locked":false,"items":[
              {"id":302,"menu_id":92,"display_name":"Steps","icon":"Layers","href":"/s","parent_id":301,"sort_order":1,"locked":false,"items":[]}]}]}
        ]}
        """;

    private const string MenuWithALockedItemBetween = """
        {"id":92,"name":"Admin","items":[
          {"id":401,"menu_id":92,"display_name":"A","icon":"X","href":"/a","parent_id":null,"sort_order":0,"locked":false,"items":[]},
          {"id":402,"menu_id":92,"display_name":"Built-in","icon":"X","href":"/b","parent_id":null,"sort_order":5,"locked":true,"items":[]},
          {"id":403,"menu_id":92,"display_name":"C","icon":"X","href":"/c","parent_id":null,"sort_order":9,"locked":false,"items":[]}
        ]}
        """;

    private const string MenuWithTwoChildren = """
        {"id":92,"name":"Admin","items":[
          {"id":299,"menu_id":92,"display_name":"Badges","icon":"Award","href":"/b","parent_id":null,"sort_order":1,"locked":false,"items":[
            {"id":301,"menu_id":92,"display_name":"Levels","icon":"Layers","href":"/l","parent_id":299,"sort_order":1,"locked":false,"items":[]},
            {"id":302,"menu_id":92,"display_name":"Steps","icon":"Layers","href":"/s","parent_id":299,"sort_order":2,"locked":false,"items":[]}]}
        ]}
        """;

    private const string MenuList = """
        [{"id":90,"name":"Public","role_id":3,"sort_order":0,"items":[]},
         {"id":91,"name":"Staff","role_id":4,"sort_order":1,"items":[]},
         {"id":92,"name":"Admin","role_id":5,"sort_order":2,"items":[
           {"id":299,"menu_id":92,"display_name":"[draft] Badges","icon":"Award","href":"/org/42/entities/badges","parent_id":null,"sort_order":1,"items":[]}]}]
        """;

    public static TheoryData<CliToolScope> RemoteScopes => new() { CliToolScope.Internal, CliToolScope.Hosted };

    public static TheoryData<CliToolScope> AllScopes => new() { CliToolScope.Local, CliToolScope.Internal, CliToolScope.Hosted };

    private static Dictionary<string, JsonElement> Args(object values) =>
        JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

    private static Task<CliRunResult> Run(CliToolScope scope, string tool, object args, MockHttpMessageHandler mock) =>
        CliCommandTool.All(scope).Single(t => t.ProtocolTool.Name == tool)
            .RunAsync(Args(args), new AnythinkClient("42", "https://api.my.anythink.cloud", new HttpClient(mock)));

    private static Dictionary<string, JsonElement> Args(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private static Task<CliRunResult> RunCli(CliToolScope scope, MockHttpMessageHandler mock, params string[] args) =>
        CliRunner.RunAsync(args, new AnythinkClient("42", "https://api.my.anythink.cloud", new HttpClient(mock)), scope);

    // Spinner text is parsed on a refresh thread, so only a response that outlasts the first refresh exercises it.
    private static MockedRequest SlowJson(MockHttpMessageHandler mock, HttpMethod method, string path, string body) =>
        mock.Expect(method, Org + path).Respond(async _ =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            return new HttpResponseMessage { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        });

    private static MockedRequest Json(MockHttpMessageHandler mock, HttpMethod method, string path, string body) =>
        mock.Expect(method, Org + path).Respond("application/json", body);

    private static MockedRequest EntityExists(MockHttpMessageHandler mock, string name) =>
        Json(mock, HttpMethod.Get, $"/entities/{name}?includeSystem=true",
            $$"""{"name":"{{name}}","table_name":"default_{{name}}","enable_rls":false,"is_system":false,"is_junction":false,"is_public":false,"lock_new_records":false,"fields":[],"id":7}""");

    private static MockedRequest NoContent(MockHttpMessageHandler mock, HttpMethod method, string path, string body) =>
        mock.Expect(method, Org + path).WithContent(body).Respond(HttpStatusCode.NoContent);

    // ── Rule: every command sends the right method, path and body ──────────────

    [Theory]
    [MemberData(nameof(AllScopes))]
    public async Task Get_ShowsEveryItemIdWithItsParent_AndWhichItemsAreLocked(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        Json(mock, HttpMethod.Get, "/menus", MenuList);

        var result = await RunCli(scope, mock, "menus", "get", "92");

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("Admin (id: 92, role: 5)").And.Contain("299").And.Contain("301").And.Contain("Levels").And.Contain("yes");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Get_AsJson_NestsChildItemsUnderTheirParent(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        Json(mock, HttpMethod.Get, "/menus", MenuList);

        var result = await Run(scope, "menus_get", new { menu_id = 92 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        var badges = json.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == 299);
        badges.GetProperty("items")[0].GetProperty("id").GetInt32().Should().Be(301);
        badges.GetProperty("items")[0].GetProperty("parent_id").GetInt32().Should().Be(299);
        json.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("id").GetInt32() == 10)
            .GetProperty("locked").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Create_PostsTheNameAndTheRole(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus", MenuList);
        mock.Expect(HttpMethod.Post, Org + "/menus").WithContent("""{"name":"Staff area","role_id":239}""")
            .Respond("application/json", """{"id":93,"name":"Staff area","role_id":239,"items":[]}""");

        var result = await Run(scope, "menus_create", new { name = "Staff area", role_id = 239 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("Staff area").And.Contain("93").And.NotContain("already has a menu");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Update_WithOnlyAName_KeepsTheCurrentRole(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus", MenuList);
        NoContent(mock, HttpMethod.Put, "/menus/92", """{"name":"Owners","role_id":5}""");

        var result = await Run(scope, "menus_update", new { menu_id = 92, name = "Owners" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Update_WithOnlyARole_KeepsTheCurrentName(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus", MenuList);
        NoContent(mock, HttpMethod.Put, "/menus/92", """{"name":"Admin","role_id":7}""");

        var result = await Run(scope, "menus_update", new { menu_id = 92, role = 7 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Update_WithNothingToChange_SendsNothing(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();

        var result = await Run(scope, "menus_update", new { menu_id = 92 }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Nothing to update");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Delete_RemovesTheMenu_AndSaysHowManyItemsGoWithIt(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        Json(mock, HttpMethod.Get, "/menus", MenuList);
        mock.Expect(HttpMethod.Delete, Org + "/menus/92").Respond(HttpStatusCode.NoContent);

        var result = await Run(scope, "menus_delete", new { menu_id = 92 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("and its 4 items, including 1 locked built-in item");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task AddItem_PostsTheEntityLinkUnderTheParent(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        EntityExists(mock, "check_ins");
        mock.Expect(HttpMethod.Post, Org + "/menus/92/items")
            .WithContent("""{"display_name":"Check Ins","icon":"Award","href":"/org/42/entities/check_ins","parent_id":299}""")
            .Respond("application/json", """{"id":302,"menu_id":92,"display_name":"Check Ins","icon":"Award","href":"/x","parent_id":299,"sort_order":2,"items":[]}""");

        var result = await Run(scope, "menus_add_item", new { menu_id = 92, entity = "check_ins", icon = "Award", parent = 299 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("302");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task UpdateItem_ChangesOnlyTheNamedFields_AndKeepsTheRest(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        NoContent(mock, HttpMethod.Put, "/menus/92/items/301",
            """{"display_name":"Tiers","icon":"Layers","href":"/org/42/entities/levels","parent_id":299}""");

        var result = await Run(scope, "menus_update_item", new { menu_id = 92, item_id = 301, name = "Tiers" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task UpdateItem_WithAnEntity_PointsTheLinkAtThatEntity(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        EntityExists(mock, "articles");
        NoContent(mock, HttpMethod.Put, "/menus/92/items/300",
            """{"display_name":"[draft] Posts","icon":"FileText","href":"/org/42/entities/articles","parent_id":0}""");

        var result = await Run(scope, "menus_update_item", new { menu_id = 92, item_id = 300, entity = "articles" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        mock.VerifyNoOutstandingExpectation();
    }

    // ── Rule: an item is never pointed at an entity that doesn't exist ──

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task AddItem_ForAnEntityThatDoesNotExist_SaysSoAndWritesNothing(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect(HttpMethod.Get, Org + "/entities/gone?includeSystem=true").Respond(HttpStatusCode.NotFound);

        var result = await Run(scope, "menus_add_item", new { menu_id = 92, entity = "gone" }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Entity 'gone' not found, so the menu item would point at nothing");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task UpdateItem_WithAnEntityThatDoesNotExist_SaysSoAndWritesNothing(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        mock.Expect(HttpMethod.Get, Org + "/entities/gone?includeSystem=true").Respond(HttpStatusCode.NotFound);

        var result = await Run(scope, "menus_update_item", new { menu_id = 92, item_id = 300, entity = "gone" }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Entity 'gone' not found, so the menu item would point at nothing");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task UpdateItem_WithAHref_DoesNotCheckForAnEntity(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        NoContent(mock, HttpMethod.Put, "/menus/92/items/300",
            """{"display_name":"[draft] Posts","icon":"FileText","href":"/org/42/settings","parent_id":0}""");

        var result = await Run(scope, "menus_update_item", new { menu_id = 92, item_id = 300, href = "/org/42/settings" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        mock.VerifyNoOutstandingExpectation();
    }

    // ── Rule: the CLI is never stricter than the API, so only a 404 stops an item pointing at an entity ──

    [Theory]
    [InlineData(403)]
    [InlineData(500)]
    public async Task AddItem_WhenTheEntityCannotBeChecked_StillAddsTheItemAndWarns(int status)
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect(HttpMethod.Get, Org + "/entities/tags?includeSystem=true").Respond((HttpStatusCode)status);
        Json(mock, HttpMethod.Post, "/menus/92/items", """{"id":303,"menu_id":92,"display_name":"Tags","icon":"Tag","href":"/x","parent_id":null,"sort_order":3,"items":[]}""");

        var result = await Run(CliToolScope.Internal, "menus_add_item", new { menu_id = 92, entity = "tags" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("303").And.Contain("Couldn't check that entity 'tags' exists");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [InlineData(403)]
    [InlineData(500)]
    public async Task UpdateItem_WhenTheEntityCannotBeChecked_StillUpdatesTheItemAndWarns(int status)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        mock.Expect(HttpMethod.Get, Org + "/entities/articles?includeSystem=true").Respond((HttpStatusCode)status);
        NoContent(mock, HttpMethod.Put, "/menus/92/items/300",
            """{"display_name":"[draft] Posts","icon":"FileText","href":"/org/42/entities/articles","parent_id":0}""");

        var result = await Run(CliToolScope.Internal, "menus_update_item", new { menu_id = 92, item_id = 300, entity = "articles" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("Couldn't check that entity 'articles' exists");
        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task AddItem_ForASystemEntity_IsNotReportedMissing()
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect(HttpMethod.Get, Org + "/entities/anythink_users?includeSystem=true")
            .Respond("application/json", """{"name":"anythink_users","table_name":"anythink_users","enable_rls":false,"is_system":true,"is_junction":false,"is_public":false,"lock_new_records":false,"fields":[],"id":1}""");
        mock.When(HttpMethod.Get, Org + "/entities/anythink_users").Respond(HttpStatusCode.NotFound);
        Json(mock, HttpMethod.Post, "/menus/92/items", """{"id":304,"menu_id":92,"display_name":"Anythink Users","icon":"Users","href":"/x","parent_id":null,"sort_order":4,"items":[]}""");

        var result = await Run(CliToolScope.Internal, "menus_add_item", new { menu_id = 92, entity = "anythink_users" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().NotContain("not found");
        mock.VerifyNoOutstandingExpectation();
    }

    // ── Rule: an item moved to a new parent ends up last among its new siblings ──

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task UpdateItem_WithAParentOfZero_MovesTheItemLastAtTheTopLevel(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        NoContent(mock, HttpMethod.Put, "/menus/92/items/301",
            """{"display_name":"Levels","icon":"Layers","href":"/org/42/entities/levels","parent_id":0}""");
        NoContent(mock, HttpMethod.Put, "/menus/92/items/reorder", """[{"item_id":301,"sort_order":3}]""");

        var result = await Run(scope, "menus_update_item", new { menu_id = 92, item_id = 301, parent = 0 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("now last at the top level");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task UpdateItem_WithANewParent_PutsTheItemAfterItsNewSiblings_KeepingTheParent(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        NoContent(mock, HttpMethod.Put, "/menus/92/items/300",
            """{"display_name":"[draft] Posts","icon":"FileText","href":"/org/42/entities/posts","parent_id":299}""");
        NoContent(mock, HttpMethod.Put, "/menus/92/items/reorder", """[{"item_id":300,"sort_order":2,"parent_id":299}]""");

        var result = await Run(scope, "menus_update_item", new { menu_id = 92, item_id = 300, parent = 299 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("now last under item 299");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task UpdateItem_UnderAParentWithNoChildren_StartsItsNumberingAtZero(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        NoContent(mock, HttpMethod.Put, "/menus/92/items/299",
            """{"display_name":"Badges","icon":"Award","href":"/org/42/entities/badges","parent_id":300}""");
        NoContent(mock, HttpMethod.Put, "/menus/92/items/reorder", """[{"item_id":299,"sort_order":0,"parent_id":300}]""");

        var result = await Run(scope, "menus_update_item", new { menu_id = 92, item_id = 299, parent = 300 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task UpdateItem_WhenPlacingTheItemLastFails_SaysItWasStillUpdated(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        mock.Expect(HttpMethod.Put, Org + "/menus/92/items/300").Respond(HttpStatusCode.NoContent);
        mock.Expect(HttpMethod.Put, Org + "/menus/92/items/reorder").Respond(HttpStatusCode.InternalServerError);

        var result = await Run(scope, "menus_update_item", new { menu_id = 92, item_id = 300, parent = 299 }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("The item was updated, but it could not be placed last");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task UpdateItem_WithAHref_SetsAnyPath(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        NoContent(mock, HttpMethod.Put, "/menus/92/items/300",
            """{"display_name":"[draft] Posts","icon":"Link","href":"/org/42/settings","parent_id":0}""");

        var result = await Run(scope, "menus_update_item", new { menu_id = 92, item_id = 300, href = "/org/42/settings", icon = "Link" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task RemoveItem_SendsADeleteForThatItemInThatMenu(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        mock.Expect(HttpMethod.Delete, Org + "/menus/92/items/300").Respond(HttpStatusCode.NoContent);

        var result = await Run(scope, "menus_remove_item", new { menu_id = 92, item_id = 300 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("300").And.Contain("removed from menu 92");
        mock.VerifyNoOutstandingExpectation();
    }

    // ── Rule: locked items keep their place; only the movable items are numbered, around them, and sent ──

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ReorderItems_NumbersTheMovedItemsAfterALockedOne_AndDoesNotSendIt(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        NoContent(mock, HttpMethod.Put, "/menus/92/items/reorder", """[{"item_id":300,"sort_order":1},{"item_id":299,"sort_order":2}]""");

        var result = await Run(scope, "menus_reorder_items", new { menu_id = 92, item_ids = "300,299" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("moved 300, 299");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ReorderItems_AroundALockedItemInTheMiddle_ReusesTheNumbersOnEachSide(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", MenuWithALockedItemBetween);
        NoContent(mock, HttpMethod.Put, "/menus/92/items/reorder", """[{"item_id":403,"sort_order":0},{"item_id":401,"sort_order":9}]""");

        var result = await Run(scope, "menus_reorder_items", new { menu_id = 92, item_ids = "403" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().NotContain("402");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ReorderItems_ForItemsAlreadyInThatOrder_SendsNothing(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);

        var result = await Run(scope, "menus_reorder_items", new { menu_id = 92, item_ids = "299,300" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("already in that order");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task ReorderItems_ForChildren_CarriesTheParentIdSoTheyStayNested(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", MenuWithTwoChildren);
        NoContent(mock, HttpMethod.Put, "/menus/92/items/reorder",
            """[{"item_id":302,"sort_order":1,"parent_id":299},{"item_id":301,"sort_order":2,"parent_id":299}]""");

        var result = await Run(scope, "menus_reorder_items", new { menu_id = 92, item_ids = "302" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Reorder_SendsEveryMenusPosition_WithTheNamedMenusFirst(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus", MenuList);
        NoContent(mock, HttpMethod.Put, "/menus/reorder",
            """[{"menu_id":92,"sort_order":0},{"menu_id":90,"sort_order":1},{"menu_id":91,"sort_order":2}]""");

        var result = await Run(scope, "menus_reorder", new { menu_ids = "92,90" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        mock.VerifyNoOutstandingExpectation();
    }

    // ── Rule: an item or menu that doesn't exist is reported by number, and nothing is sent ──

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task RemoveItem_ForAnItemThatDoesNotExist_ReportsWhichItemWasNotFound(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);

        var result = await Run(scope, "menus_remove_item", new { menu_id = 92, item_id = 555 }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Menu item 555 not found in menu 92");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task RemoveItem_ForAnItemInAnotherMenu_IsNotRemoved(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/91", """{"id":91,"name":"Staff","items":[]}""");

        var result = await Run(scope, "menus_remove_item", new { menu_id = 91, item_id = 300 }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Menu item 300 not found in menu 91");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [InlineData("menus_get", """{"menu_id":7}""")]
    [InlineData("menus_delete", """{"menu_id":7}""")]
    [InlineData("menus_remove_item", """{"menu_id":7,"item_id":1}""")]
    [InlineData("menus_update_item", """{"menu_id":7,"item_id":1,"name":"x"}""")]
    [InlineData("menus_reorder_items", """{"menu_id":7,"item_ids":"1"}""")]
    public async Task AMenuThatDoesNotExist_IsReportedByNumber(string tool, string json)
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect(HttpMethod.Get, Org + "/menus/7").Respond(HttpStatusCode.NotFound);

        var result = await CliCommandTool.All(CliToolScope.Internal).Single(t => t.ProtocolTool.Name == tool)
            .RunAsync(Args(json), new AnythinkClient("42", "https://api.my.anythink.cloud", new HttpClient(mock)));

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Menu 7 not found");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Update_ForAMenuThatDoesNotExist_ReportsItAndSendsNothing(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus", MenuList);

        var result = await Run(scope, "menus_update", new { menu_id = 7, name = "x" }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Menu 7 not found");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Reorder_WithAMenuThatDoesNotExist_ReportsItAndSendsNothing(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus", MenuList);

        var result = await Run(scope, "menus_reorder", new { menu_ids = "92,7" }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Menu 7 not found");
        mock.VerifyNoOutstandingExpectation();
    }

    // ── Rule: deleting a menu says what goes with it, including the locked built-in items and the role's menu ──

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Delete_OfTheOnlyMenuForARole_SaysTheRoleIsLeftWithNoMenu(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        Json(mock, HttpMethod.Get, "/menus", MenuList);
        mock.Expect(HttpMethod.Delete, Org + "/menus/92").Respond(HttpStatusCode.NoContent);

        var result = await Run(scope, "menus_delete", new { menu_id = 92 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("Role 5 now has no menu");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Delete_OfOneOfTwoMenusForARole_DoesNotSayTheRoleIsLeftWithNoMenu(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        Json(mock, HttpMethod.Get, "/menus", """[{"id":91,"name":"Other","role_id":5,"items":[]},{"id":92,"name":"Admin","role_id":5,"items":[]}]""");
        mock.Expect(HttpMethod.Delete, Org + "/menus/92").Respond(HttpStatusCode.NoContent);

        var result = await Run(scope, "menus_delete", new { menu_id = 92 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().NotContain("no menu");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Delete_WhenTheMenuListCannotBeRead_StillDeletesWithoutTheRoleNote(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        mock.Expect(HttpMethod.Get, Org + "/menus").Respond(HttpStatusCode.InternalServerError);
        mock.Expect(HttpMethod.Delete, Org + "/menus/92").Respond(HttpStatusCode.NoContent);

        var result = await Run(scope, "menus_delete", new { menu_id = 92 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("deleted").And.NotContain("no menu");
        mock.VerifyNoOutstandingExpectation();
    }

    // ── Rule: a role is shown one menu, so giving it a second one warns that it may not appear ──

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Create_ForARoleThatAlreadyHasAMenu_CreatesItAndWarnsItMayNotAppear(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus", MenuList);
        Json(mock, HttpMethod.Post, "/menus", """{"id":95,"name":"Second","role_id":3,"items":[]}""");

        var result = await Run(scope, "menus_create", new { name = "Second", role_id = 3 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("Second").And.Contain("Role 3 already has a menu ('Public', id: 90)").And.Contain("may not appear");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Create_WhenTheMenuListCannotBeRead_StillCreatesTheMenuWithoutAWarning(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        mock.Expect(HttpMethod.Get, Org + "/menus").Respond(HttpStatusCode.InternalServerError);
        Json(mock, HttpMethod.Post, "/menus", """{"id":95,"name":"Second","role_id":3,"items":[]}""");

        var result = await Run(scope, "menus_create", new { name = "Second", role_id = 3 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().NotContain("already has a menu");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Create_WarningNamesTheOtherMenuLiterally_EvenWithSquareBrackets(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus", """[{"id":90,"name":"[old] Public","role_id":3,"items":[]}]""");
        Json(mock, HttpMethod.Post, "/menus", """{"id":95,"name":"Second","role_id":3,"items":[]}""");

        var result = await Run(scope, "menus_create", new { name = "Second", role_id = 3 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("('[old] Public', id: 90)");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Update_MovingAMenuToARoleThatHasOne_WarnsItMayNotAppear(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus", MenuList);
        NoContent(mock, HttpMethod.Put, "/menus/92", """{"name":"Admin","role_id":4}""");

        var result = await Run(scope, "menus_update", new { menu_id = 92, role = 4 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("Role 4 already has a menu ('Staff', id: 91)");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Update_KeepingTheSameRole_DoesNotWarn(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus", MenuList);
        NoContent(mock, HttpMethod.Put, "/menus/92", """{"name":"Admin","role_id":5}""");

        var result = await Run(scope, "menus_update", new { menu_id = 92, role = 5 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().NotContain("already has a menu");
    }

    // ── Rule: get --json keeps text readable and says which role the menu is for ──

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Get_AsJson_IncludesTheMenusRole(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        Json(mock, HttpMethod.Get, "/menus", MenuList);

        var result = await Run(scope, "menus_get", new { menu_id = 92 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        json.RootElement.GetProperty("role_id").GetInt32().Should().Be(5);
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Get_AsJson_WhenTheMenuListCannotBeRead_LeavesTheRoleNull(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        mock.Expect(HttpMethod.Get, Org + "/menus").Respond(HttpStatusCode.InternalServerError);

        var result = await Run(scope, "menus_get", new { menu_id = 92 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        using var json = JsonDocument.Parse(result.Output);
        json.RootElement.GetProperty("role_id").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Get_AsJson_DoesNotEscapeQuotesAngleBracketsOrNonAsciiInLabels(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", """
            {"id":92,"name":"Admin","items":[{"id":1,"menu_id":92,"display_name":"Fish & \"chips\" <b> café","icon":"X","href":"/a?b=1&c=2","parent_id":null,"sort_order":0,"items":[]}]}
            """);
        Json(mock, HttpMethod.Get, "/menus", MenuList);

        var result = await Run(scope, "menus_get", new { menu_id = 92 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("\"display_name\": \"Fish & \\\"chips\\\" <b> café\"").And.Contain("/a?b=1&c=2").And.NotContain("\\u");
    }

    // ── Rule: removing an item that has children says what goes with it ─────────

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task RemoveItem_WithChildren_SaysTheyAreRemovedToo(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        mock.Expect(HttpMethod.Delete, Org + "/menus/92/items/299").Respond(HttpStatusCode.NoContent);

        var result = await Run(scope, "menus_remove_item", new { menu_id = 92, item_id = 299 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("and its 1 child item (301)");
        mock.VerifyNoOutstandingExpectation();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task RemoveItem_WhoseChildrenHaveChildren_IsRefusedBeforeAnythingIsSent(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", MenuWithGrandchild);

        var result = await Run(scope, "menus_remove_item", new { menu_id = 92, item_id = 299 }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("can't be removed: child items that are locked or have items of their own (301)");
        mock.VerifyNoOutstandingExpectation();
    }

    // ── Rule: locked built-in items are never changed, removed or moved ─────────

    [Theory]
    [InlineData("menus_remove_item", "can't be removed")]
    [InlineData("menus_update_item", "can't be changed")]
    [InlineData("menus_reorder_items", "can't be moved")]
    public async Task ALockedItem_IsRefusedRatherThanSilentlyIgnored(string tool, string message)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        object args = tool == "menus_reorder_items"
            ? new { menu_id = 92, item_ids = "10" }
            : tool == "menus_update_item" ? new { menu_id = 92, item_id = 10, name = "x" } : new { menu_id = 92, item_id = 10 };

        var result = await Run(CliToolScope.Internal, tool, args, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Menu item 10 is locked").And.Contain(message);
        mock.VerifyNoOutstandingExpectation();
    }

    // ── Rule: a re-parent can't create a loop or point at nothing ───────────────

    [Theory]
    [InlineData(299, 299, "its own parent")]
    [InlineData(299, 301, "sits inside item 299")]
    [InlineData(300, 999, "Parent menu item 999 not found in menu 92")]
    public async Task UpdateItem_WithAParentThatWouldBreakTheTree_IsRefused(int item, int parent, string message)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);

        var result = await Run(CliToolScope.Internal, "menus_update_item", new { menu_id = 92, item_id = item, parent }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain(message);
        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task UpdateItem_WithBothAnEntityAndAHref_IsRefused()
    {
        var mock = new MockHttpMessageHandler();

        var result = await Run(CliToolScope.Internal, "menus_update_item", new { menu_id = 92, item_id = 300, entity = "a", href = "/b" }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("--entity or --href, not both");
    }

    [Fact]
    public async Task UpdateItem_WithNothingToChange_SendsNothing()
    {
        var mock = new MockHttpMessageHandler();

        var result = await Run(CliToolScope.Internal, "menus_update_item", new { menu_id = 92, item_id = 300 }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("Nothing to update");
    }

    // ── Rule: a reorder names real, distinct items that share one parent ────────

    [Theory]
    [InlineData("299,555", "Menu item 555 not found in menu 92")]
    [InlineData("299,301", "Items 299 and 301 have different parents")]
    [InlineData("299,299", "Item 299 is listed more than once")]
    [InlineData("299,abc", "'abc' isn't one")]
    [InlineData("299,-4", "'-4' isn't one")]
    [InlineData(",", "Give at least one item ID")]
    public async Task ReorderItems_WithAnInvalidList_IsRefusedAndSendsNothing(string ids, string message)
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, Org + "/menus/92").Respond("application/json", Menu92);

        var result = await Run(CliToolScope.Internal, "menus_reorder_items", new { menu_id = 92, item_ids = ids }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain(message);
    }

    [Theory]
    [InlineData("90,x", "'x' isn't one")]
    [InlineData("90,90", "Menu 90 is listed more than once")]
    public async Task Reorder_WithAnInvalidList_IsRefusedAndSendsNothing(string ids, string message)
    {
        var mock = new MockHttpMessageHandler();

        var result = await Run(CliToolScope.Internal, "menus_reorder", new { menu_ids = ids }, mock);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain(message);
    }

    // ── Rule: a name with [square brackets] is shown literally and doesn't crash ──

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Get_ShowsALabelWithSquareBracketsLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        Json(mock, HttpMethod.Get, "/menus", MenuList);

        var result = await RunCli(scope, mock, "menus", "get", "92");

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("[draft] Posts").And.NotContain("[[");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task List_ShowsALabelWithSquareBracketsLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus", MenuList);

        var result = await Run(scope, "menus_list", new { }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("[draft] Badges").And.NotContain("[[");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task AddItem_WithABracketedLabel_ConfirmsItLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        EntityExists(mock, "tags");
        SlowJson(mock, HttpMethod.Post, "/menus/92/items",
            """{"id":303,"menu_id":92,"display_name":"[draft] Tags","icon":"Tag","href":"/x","parent_id":null,"sort_order":3,"items":[]}""");

        var result = await Run(scope, "menus_add_item", new { menu_id = 92, entity = "tags", name = "[draft] Tags" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("[draft] Tags");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Create_WithABracketedName_ConfirmsItLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus", MenuList);
        SlowJson(mock, HttpMethod.Post, "/menus", """{"id":94,"name":"[beta] Staff","role_id":4,"items":[]}""");

        var result = await Run(scope, "menus_create", new { name = "[beta] Staff", role_id = 4 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("[beta] Staff");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Update_WithABracketedName_ConfirmsItLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus", MenuList);
        NoContent(mock, HttpMethod.Put, "/menus/92", """{"name":"[beta] Admin","role_id":5}""");

        var result = await Run(scope, "menus_update", new { menu_id = 92, name = "[beta] Admin" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("[beta] Admin");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task UpdateItem_WithABracketedLabel_ConfirmsItLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        mock.Expect(HttpMethod.Put, Org + "/menus/92/items/299").Respond(HttpStatusCode.NoContent);

        var result = await Run(scope, "menus_update_item", new { menu_id = 92, item_id = 299, name = "[new] Badges" }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("[new] Badges");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task RemoveItem_OfABracketedLabel_ConfirmsItLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", Menu92);
        mock.Expect(HttpMethod.Delete, Org + "/menus/92/items/300").Respond(HttpStatusCode.NoContent);

        var result = await Run(scope, "menus_remove_item", new { menu_id = 92, item_id = 300 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("[draft] Posts");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Delete_OfABracketedMenuName_ConfirmsItLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        Json(mock, HttpMethod.Get, "/menus/92", """{"id":92,"name":"[old] Admin","items":[]}""");
        Json(mock, HttpMethod.Get, "/menus", MenuList);
        mock.Expect(HttpMethod.Delete, Org + "/menus/92").Respond(HttpStatusCode.NoContent);

        var result = await Run(scope, "menus_delete", new { menu_id = 92 }, mock);

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("[old] Admin");
    }

    // ── Rule: tool hints follow the verb ────────────────────────────────────────

    [Theory]
    [MemberData(nameof(AllScopes))]
    public void MenuTools_AreHintedByWhatTheyDo(CliToolScope scope)
    {
        var tools = CliCommandTool.All(scope).Where(t => t.ProtocolTool.Name.StartsWith("menus_")).ToDictionary(t => t.ProtocolTool.Name);

        tools.Keys.Should().BeEquivalentTo(
            "menus_list", "menus_get", "menus_create", "menus_update", "menus_delete",
            "menus_add_item", "menus_update_item", "menus_remove_item", "menus_reorder_items", "menus_reorder");

        foreach (var name in new[] { "menus_list", "menus_get" })
            tools[name].ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue(name);
        foreach (var name in new[] { "menus_create", "menus_add_item" })
        {
            tools[name].ProtocolTool.Annotations!.ReadOnlyHint.Should().BeFalse(name);
            tools[name].ProtocolTool.Annotations!.DestructiveHint.Should().BeFalse(name);
        }
        foreach (var name in new[] { "menus_update", "menus_update_item", "menus_reorder", "menus_reorder_items", "menus_delete", "menus_remove_item" })
        {
            tools[name].ProtocolTool.Annotations!.ReadOnlyHint.Should().BeFalse(name);
            tools[name].ProtocolTool.Annotations!.DestructiveHint.Should().BeTrue(name);
        }
    }

    // ── Rule: remote callers can pass id lists and names with spaces, but nothing that looks like an option ──

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public void RemoteCallers_CanPassIdListsAndMenuNames(CliToolScope scope)
    {
        var tool = (string name) => CliCommandTool.All(scope).Single(t => t.ProtocolTool.Name == name);

        tool("menus_reorder_items").BuildArgs(Args(new { menu_id = 92, item_ids = "301,299,300" }))
            .Should().Equal("menus", "reorder-items", "92", "301,299,300");
        tool("menus_reorder").BuildArgs(Args(new { menu_ids = "92,90" })).Should().Equal("menus", "reorder", "92,90");
        tool("menus_create").BuildArgs(Args(new { name = "Staff area", role_id = 4 })).Should().Equal("menus", "create", "Staff area", "4");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public void RemoteCallers_CannotSmuggleAnOptionIntoAnIdList(CliToolScope scope)
    {
        var tool = CliCommandTool.All(scope).Single(t => t.ProtocolTool.Name == "menus_reorder");

        var act = () => tool.BuildArgs(Args(new { menu_ids = "--yes" }));

        act.Should().Throw<ArgumentException>().WithMessage("*can't start with '-'*");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public void ConfirmationIsAutomaticForTheDestructiveMenuTools(CliToolScope scope)
    {
        var tool = (string name) => CliCommandTool.All(scope).Single(t => t.ProtocolTool.Name == name);

        tool("menus_delete").BuildArgs(Args(new { menu_id = 92 })).Should().Equal("menus", "delete", "92", "--yes");
        tool("menus_remove_item").BuildArgs(Args(new { menu_id = 92, item_id = 300 })).Should().Equal("menus", "remove-item", "92", "300", "--yes");
        tool("menus_get").BuildArgs(Args(new { menu_id = 92 })).Should().Equal("menus", "get", "92", "--json");
    }
}
