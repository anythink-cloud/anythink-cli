using System.Text.Json;
using AnythinkCli.Client;
using AnythinkMcp.Cli;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

public class CliCommandToolTests
{
    private const string ApiUrl = "https://api.my.anythink.cloud";

    private static CliCommandTool? Find(string name, CliToolScope scope) =>
        CliCommandTool.All(scope).SingleOrDefault(t => t.ProtocolTool.Name == name);

    private static CliCommandTool Tool(string name, CliToolScope scope = CliToolScope.Remote) => Find(name, scope)!;

    private static Dictionary<string, JsonElement> Args(object values) =>
        JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

    private static AnythinkClient Client(MockHttpMessageHandler mock) => new("42", ApiUrl, new HttpClient(mock));

    private static IEnumerable<string> Properties(CliCommandTool tool) =>
        tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name);

    [Fact]
    public void Model_ReadsArgumentsAndOptionsFromTheCli()
    {
        var dataList = CliCommandModel.All.Single(c => c.Key == "data list");

        dataList.Description.Should().Be("List records in an entity");
        dataList.Parameters.Should().ContainEquivalentOf(new
        {
            Name = "entity", Kind = CliParameterKind.Argument, Position = 0, Required = true
        });
        dataList.Parameters.Should().ContainEquivalentOf(new { Name = "limit", Token = "--limit", Kind = CliParameterKind.Scalar });
    }

    [Theory]
    [InlineData(CliToolScope.Local)]
    [InlineData(CliToolScope.Remote)]
    public void EveryTool_HasAUniqueNameATitleAndAnExplicitHint(CliToolScope scope)
    {
        var tools = CliCommandTool.All(scope);

        tools.Select(t => t.ProtocolTool.Name).Should().OnlyHaveUniqueItems();
        tools.Should().AllSatisfy(t =>
        {
            t.ProtocolTool.Annotations!.Title.Should().NotBeNullOrWhiteSpace();
            (t.ProtocolTool.Annotations.ReadOnlyHint == true || t.ProtocolTool.Annotations.DestructiveHint.HasValue)
                .Should().BeTrue($"{t.ProtocolTool.Name} needs readOnlyHint or destructiveHint");
        });
    }

    [Theory]
    [InlineData("login")]
    [InlineData("signup")]
    [InlineData("config_show")]
    [InlineData("accounts_list")]
    [InlineData("projects_list")]
    [InlineData("plans")]
    [InlineData("migrate")]
    [InlineData("cli_xmldoc")]
    public void PlatformAndCrossProjectCommands_AreLeftOutEverywhere(string name)
    {
        Find(name, CliToolScope.Local).Should().BeNull();
        Find(name, CliToolScope.Remote).Should().BeNull();
    }

    [Theory]
    [InlineData("files_upload")]
    [InlineData("workflows_seed")]
    [InlineData("pay_connect")]
    [InlineData("oauth_google_configure")]
    [InlineData("integrations_oauth_connect")]
    public void CommandsThatUseTheLocalMachine_AreLocalOnly(string name)
    {
        Find(name, CliToolScope.Local).Should().NotBeNull();
        Find(name, CliToolScope.Remote).Should().BeNull();
    }

    [Fact]
    public void FileOptions_AreHiddenRemotely()
    {
        Properties(Tool("workflows_create", CliToolScope.Local)).Should().Contain("filter_file");
        Properties(Tool("workflows_create")).Should().NotContain("filter_file").And.Contain("filter");
    }

    [Theory]
    [InlineData("entities_list", true, null)]
    [InlineData("entities_delete", false, true)]
    [InlineData("entities_create", false, false)]
    public void Annotations_FollowTheCommandVerb(string name, bool readOnly, bool? destructive)
    {
        var annotations = Tool(name).ProtocolTool.Annotations!;

        annotations.ReadOnlyHint.Should().Be(readOnly);
        annotations.DestructiveHint.Should().Be(destructive);
    }

    [Fact]
    public void Arguments_ArePositionalAndOptionsCarryTheirValue()
    {
        var args = Tool("workflows_create").BuildArgs(Args(new { cron = "0 9 * * *", name = "Nightly", enabled = true }));

        args.Should().Equal("workflows", "create", "Nightly", "--cron=0 9 * * *", "--enabled");
    }

    [Fact]
    public void JsonAndConfirmationFlags_AreAddedAutomatically()
    {
        Tool("data_list").BuildArgs(Args(new { entity = "posts" })).Should().Equal("data", "list", "posts", "--json");
        Tool("entities_delete").BuildArgs(Args(new { name = "posts" })).Should().Equal("entities", "delete", "posts", "--yes");
        Properties(Tool("entities_delete")).Should().NotContain("yes");
    }

    [Theory]
    [InlineData("""{"name":"--filter-file=/etc/passwd"}""", "can't start with '-'")]
    [InlineData("""{"name":"Nightly","filter_file":"/etc/passwd"}""", "Unknown parameter: filter_file")]
    [InlineData("""{"cron":"0 9 * * *"}""", "'name' is required")]
    public async Task UnsafeOrInvalidInput_IsRefusedBeforeTheCliRuns(string json, string error)
    {
        var mock = new MockHttpMessageHandler();
        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

        var result = await Tool("workflows_create").RunAsync(arguments, Client(mock));

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain(error);
        mock.GetMatchCount(mock.When("*")).Should().Be(0);
    }

    [Fact]
    public async Task Run_ReturnsTheCommandsOutput()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"{ApiUrl}/org/42/entities").Respond("application/json", """[{"name":"posts","table_name":"default_posts","fields":[]}]""");

        var result = await Tool("entities_list").RunAsync(Args(new { }), Client(mock));

        result.ExitCode.Should().Be(0);
        result.Output.Should().Contain("posts");
    }

    [Fact]
    public async Task ConcurrentRuns_EachCaptureOnlyTheirOwnOutput()
    {
        static MockHttpMessageHandler Project(string title)
        {
            var mock = new MockHttpMessageHandler();
            mock.When($"{ApiUrl}/org/42/entities/posts/items*").Respond("application/json",
                $$"""{"items":[{"id":1,"title":"{{title}}"}],"total_items":1,"total_pages":1,"has_next_page":false,"page":1,"page_size":20}""");
            return mock;
        }

        var runs = Enumerable.Range(0, 8)
            .Select(i => Tool("data_list").RunAsync(Args(new { entity = "posts" }), Client(Project($"title-{i}"))))
            .ToList();
        var results = await Task.WhenAll(runs);

        for (var i = 0; i < results.Length; i++)
        {
            results[i].Output.Should().Contain($"title-{i}");
            results[i].Output.Should().NotContainAny(Enumerable.Range(0, 8).Where(j => j != i).Select(j => $"title-{j}\""));
        }
    }
}
