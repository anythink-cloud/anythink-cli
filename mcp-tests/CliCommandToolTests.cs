using System.Text.Json;
using AnythinkCli.Client;
using AnythinkMcp.Cli;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

public partial class CliCommandToolTests
{
    private const string ApiUrl = "https://api.my.anythink.cloud";

    private static CliCommandTool? Find(string name, CliToolScope scope) =>
        CliCommandTool.All(scope).SingleOrDefault(t => t.ProtocolTool.Name == name);

    private static CliCommandTool Tool(string name, CliToolScope scope = CliToolScope.Internal) => Find(name, scope)!;

    private static Dictionary<string, JsonElement> Args(object values) =>
        JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

    private static AnythinkClient Client(MockHttpMessageHandler mock) => new("42", ApiUrl, new HttpClient(mock));

    private static MockedRequest AnyRequest(MockHttpMessageHandler mock) =>
        mock.When("*").Respond("application/json", EmptyPage);

    private const string EmptyPage = """{"items":[],"total_items":0,"total_pages":0,"has_next_page":false,"page":1,"page_size":20}""";

    public static TheoryData<CliToolScope> RemoteScopes => new() { CliToolScope.Internal, CliToolScope.Hosted };

    private static readonly CliToolScope[] RemoteScopeList = [CliToolScope.Internal, CliToolScope.Hosted];

    private static IEnumerable<string> Properties(CliCommandTool tool) =>
        tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject().Select(p => p.Name);

    [Fact]
    public void Model_ReadsArgumentsAndOptionsFromTheCli()
    {
        var dataList = CliCommandModel.All.Single(c => c.Key == "data list");

        dataList.Description.Should().Be("List records in an entity");
        dataList.Parameters.Should().ContainEquivalentOf(new
        {
            Name = "entity",
            Kind = CliParameterKind.Argument,
            Position = 0,
            Required = true
        });
        dataList.Parameters.Should().ContainEquivalentOf(new { Name = "limit", Token = "--limit", Kind = CliParameterKind.Scalar });
    }

    [Theory]
    [InlineData(CliToolScope.Local)]
    [InlineData(CliToolScope.Internal)]
    [InlineData(CliToolScope.Hosted)]
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
        Find(name, CliToolScope.Internal).Should().BeNull();
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
        Find(name, CliToolScope.Internal).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public void ACommandThatReachesAnyRoute_IsLocalOnly(CliToolScope remote)
    {
        Find("fetch", CliToolScope.Local).Should().NotBeNull();
        Find("fetch", remote).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public void FileOptions_AreHiddenRemotely(CliToolScope remote)
    {
        Properties(Tool("workflows_create", CliToolScope.Local)).Should().Contain("filter_file");
        Properties(Tool("workflows_create", remote)).Should().NotContain("filter_file").And.Contain("filter");
    }

    [Theory]
    [InlineData("entities_list", true, null)]
    [InlineData("entities_delete", false, true)]
    [InlineData("entities_create", false, false)]
    [InlineData("workflows_export", true, null)]
    [InlineData("integrations_oauth_configure", false, true)]
    [InlineData("entities_update", false, true)]
    [InlineData("workflows_step_link", false, true)]
    [InlineData("data_rls", false, true)]
    public void Annotations_FollowTheCommandVerb(string name, bool readOnly, bool? destructive)
    {
        foreach (var scope in RemoteScopeList)
        {
            var annotations = Tool(name, scope).ProtocolTool.Annotations!;

            annotations.ReadOnlyHint.Should().Be(readOnly, scope.ToString());
            annotations.DestructiveHint.Should().Be(destructive, scope.ToString());
        }
    }

    // ── Rule: a command that can write a local file isn't read-only where that option is offered ──

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public void WorkflowsExport_IsReadOnlyOnlyWhereItsOutputOptionIsHidden(CliToolScope scope)
    {
        var local = Tool("workflows_export", CliToolScope.Local);
        var remote = Tool("workflows_export", scope);

        Properties(local).Should().Contain("output");
        local.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeFalse();
        local.ProtocolTool.Annotations.DestructiveHint.Should().BeTrue();
        Properties(remote).Should().NotContain("output");
        remote.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue();
    }

    // ── Rule: a command nobody classified is treated as destructive ────────────

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("configure")]
    [InlineData("reset")]
    [InlineData("")]
    public void AVerbNobodyClassified_IsDestructive(string verb)
    {
        var command = new CliCommand(["things", verb], "", []);

        foreach (var scope in new[] { CliToolScope.Local, CliToolScope.Internal })
        {
            CliToolPolicy.IsReadOnly(command, scope).Should().BeFalse();
            CliToolPolicy.IsDestructive(command, scope).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData("create")]
    [InlineData("add")]
    [InlineData("enable")]
    public void AnAdditiveVerb_IsNeitherReadOnlyNorDestructive(string verb)
    {
        var command = new CliCommand(["things", verb], "", []);

        CliToolPolicy.IsReadOnly(command, CliToolScope.Internal).Should().BeFalse();
        CliToolPolicy.IsDestructive(command, CliToolScope.Internal).Should().BeFalse();
    }

    [Theory]
    [InlineData("step-link")]
    [InlineData("rls")]
    public void AVerbThatOverwritesWhatIsThere_IsDestructive(string verb) =>
        CliToolPolicy.IsDestructive(new CliCommand(["things", verb], "", []), CliToolScope.Internal).Should().BeTrue();

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
        var any = AnyRequest(mock);
        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

        var result = await Tool("workflows_create").RunAsync(arguments, Client(mock));

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain(error);
        mock.GetMatchCount(any).Should().Be(0);
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
    public async Task ConcurrentRuns_EachUseTheirOwnCredentialsAndCaptureOnlyTheirOwnOutput()
    {
        const int runs = 8;
        var handler = new RendezvousHandler(runs);
        var tool = Tool("data_list");

        var results = await Task.WhenAll(Enumerable.Range(0, runs).Select(i =>
        {
            var http = new HttpClient(handler);
            http.DefaultRequestHeaders.Authorization = new("Bearer", $"token-{i}");
            var client = new AnythinkClient($"{100 + i}", ApiUrl, http);
            return Task.Run(() => tool.RunAsync(Args(new { entity = "posts" }), client));
        })).WaitAsync(TimeSpan.FromSeconds(30));

        handler.MostInFlight.Should().Be(runs, "every run has to be in flight at the same time for this to prove isolation");
        for (var i = 0; i < runs; i++)
        {
            results[i].ExitCode.Should().Be(0, results[i].Output);
            results[i].Output.Should().Contain($"org-{100 + i}-token-{i}\"");
            results[i].Output.Should().NotContainAny(Enumerable.Range(0, runs).Where(j => j != i).Select(j => $"org-{100 + j}-"));
        }
    }

    private sealed class RendezvousHandler(int parties) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _allArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public int MostInFlight => _arrived;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrived) == parties)
                _allArrived.SetResult();
            await _allArrived.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);

            var org = request.RequestUri!.Segments[2].TrimEnd('/');
            var token = request.Headers.Authorization!.Parameter;
            return new HttpResponseMessage
            {
                Content = new StringContent(
                    $$"""{"items":[{"id":1,"title":"org-{{org}}-{{token}}"}],"total_items":1,"total_pages":1,"has_next_page":false,"page":1,"page_size":20}""",
                    System.Text.Encoding.UTF8, "application/json")
            };
        }
    }

    [Theory]
    [InlineData(" --yes")]
    [InlineData("a b")]
    [InlineData("\"quoted\"")]
    [InlineData("@file")]
    [InlineData("a=b")]
    [InlineData("--")]
    [InlineData("x --json")]
    public async Task OptionValues_ReachTheApiAsOneValue(string description)
    {
        string? body = null;
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{ApiUrl}/org/42/workflows").Respond(async req =>
        {
            body = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage { Content = new StringContent("""{"id":7,"name":"Nightly","trigger":"Manual"}""") };
        });

        var result = await Tool("workflows_create").RunAsync(
            Args(new { name = "Nightly", trigger = "Manual", description }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        JsonDocument.Parse(body!).RootElement.GetProperty("description").GetString().Should().Be(description);
    }

    [Fact]
    public async Task ACommandThatWouldPrompt_FailsInsteadOfWaitingForInput()
    {
        var mock = new MockHttpMessageHandler();
        var any = AnyRequest(mock);

        var run = Tool("secrets_create").RunAsync(Args(new { key = "API_TOKEN" }), Client(mock));

        (await run.WaitAsync(TimeSpan.FromSeconds(10))).ExitCode.Should().NotBe(0);
        mock.GetMatchCount(any).Should().Be(0);
    }

    [Fact]
    public async Task PublicSearch_FromARequestClient_IsSentWithoutTheUsersToken()
    {
        var mock = new MockHttpMessageHandler();
        string? publicAuth = "unset", privateAuth = null;
        const string empty = """{"items":[],"page":1,"page_size":20,"total_items":0,"total_pages":0,"has_next_page":false,"has_previous_page":false}""";
        mock.When($"{ApiUrl}/org/42/search/public*").Respond(req =>
        {
            publicAuth = req.Headers.Authorization?.ToString();
            return new HttpResponseMessage { Content = new StringContent(empty) };
        });
        mock.When($"{ApiUrl}/org/42/search").Respond(req =>
        {
            privateAuth = req.Headers.Authorization?.ToString();
            return new HttpResponseMessage { Content = new StringContent(empty) };
        });
        var client = new McpClientFactory(null, mock).GetClient(new HostedCredentials { OrgId = "42", InstanceUrl = ApiUrl, Token = "user-token" });

        await client.SearchAsync("q=x", isPublic: true);
        await client.SearchAsync("q=x");

        publicAuth.Should().BeNull();
        privateAuth.Should().Be("Bearer user-token");
    }

    // ── Rule: a remote caller can't steer a request off the project's API root ──

    [Theory]
    [InlineData("../x")]
    [InlineData("../../43/entities/posts")]
    [InlineData("a?b")]
    [InlineData("a/b")]
    [InlineData("a#b")]
    [InlineData("..")]
    [InlineData("%2e%2e\\api-keys")]
    [InlineData("posts\\x")]
    [InlineData("%2e%2e")]
    [InlineData("a%2Fb")]
    [InlineData("a b")]
    [InlineData("a\nb")]
    [InlineData("é")]
    [InlineData("")]
    public async Task RemotePositionalWithPathSyntax_IsAToolError_AndNothingIsSent(string entity)
    {
        var mock = new MockHttpMessageHandler();
        var any = AnyRequest(mock);

        foreach (var scope in RemoteScopeList)
            foreach (var tool in new[] { "data_list", "data_rls" })
            {
                var arguments = tool == "data_rls" ? Args(new { entity, id = 1 }) : Args(new { entity });

                var result = await Tool(tool, scope).RunAsync(arguments, Client(mock));

                result.ExitCode.Should().Be(1, $"{scope} {tool}");
                result.Output.Should().Contain("can only contain letters, digits");
            }

        mock.GetMatchCount(any).Should().Be(0);
    }

    [Theory]
    [InlineData("posts")]
    [InlineData("blog_posts")]
    [InlineData("my-entity.v2")]
    public async Task RemotePositional_PlainIdentifier_IsSent(string entity)
    {
        foreach (var scope in RemoteScopeList)
        {
            var mock = new MockHttpMessageHandler();
            var sent = new List<Uri>();
            mock.When("*").Respond(req =>
            {
                sent.Add(req.RequestUri!);
                return new HttpResponseMessage { Content = new StringContent(EmptyPage) };
            });

            var result = await Tool("data_list", scope).RunAsync(Args(new { entity }), Client(mock));

            result.ExitCode.Should().Be(0, result.Output);
            sent.Should().ContainSingle().Which.AbsolutePath.Should().Be($"/org/42/entities/{entity}/items");
        }
    }

    [Theory]
    [InlineData("a/b", "a%2Fb")]
    [InlineData("posts\\x", "posts%5Cx")]
    [InlineData("%2e%2e\\api-keys", "%252e%252e%5Capi-keys")]
    public async Task LocalDataRls_SendsTheEntityAsOneEscapedSegment(string entity, string escaped)
    {
        var mock = new MockHttpMessageHandler();
        var sent = new List<(HttpMethod Method, Uri Uri, string Body)>();
        mock.When("*").Respond(async req =>
        {
            sent.Add((req.Method, req.RequestUri!, req.Content is null ? "" : await req.Content.ReadAsStringAsync()));
            return new HttpResponseMessage { Content = new StringContent("[]") };
        });

        var listed = await Tool("data_rls", CliToolScope.Local).RunAsync(Args(new { entity, id = 1 }), Client(mock));
        var set = await Tool("data_rls", CliToolScope.Local).RunAsync(Args(new { entity, id = 1, user = 7, @readonly = true }), Client(mock));

        listed.ExitCode.Should().Be(0, listed.Output);
        set.ExitCode.Should().Be(0, set.Output);
        sent.Select(s => s.Uri.AbsolutePath).Should().OnlyContain(path => path == $"/org/42/entities/{escaped}/items/1/rls-users");
        sent.Select(s => s.Method).Should().Equal(HttpMethod.Get, HttpMethod.Put);
        JsonDocument.Parse(sent[1].Body).RootElement.GetProperty("user_id").GetInt32().Should().Be(7);
        JsonDocument.Parse(sent[1].Body).RootElement.GetProperty("readonly").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task RemotePositional_FreeText_MayContainPunctuation(CliToolScope scope)
    {
        string? body = null;
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{ApiUrl}/org/42/workflows").Respond(async req =>
        {
            body = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage { Content = new StringContent("""{"id":7,"name":"x","trigger":"Manual"}""") };
        });

        var result = await Tool("workflows_create", scope).RunAsync(Args(new { name = "Sales / support? #1", trigger = "Manual" }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        JsonDocument.Parse(body!).RootElement.GetProperty("name").GetString().Should().Be("Sales / support? #1");
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a?b")]
    [InlineData("a#b")]
    public async Task LocalPositional_ReachesTheApiAsOneEscapedPathSegment(string entity)
    {
        var mock = new MockHttpMessageHandler();
        var sent = new List<Uri>();
        mock.When("*").Respond(req =>
        {
            sent.Add(req.RequestUri!);
            return new HttpResponseMessage { Content = new StringContent(EmptyPage) };
        });

        await Tool("data_list", CliToolScope.Local).RunAsync(Args(new { entity }), Client(mock));

        var uri = sent.Should().ContainSingle().Subject;
        uri.Host.Should().Be("api.my.anythink.cloud");
        uri.Segments.Should().HaveCount(6);
        uri.Segments[..4].Should().Equal("/", "org/", "42/", "entities/");
    }

    [Theory]
    [InlineData("/../../43/users")]
    [InlineData("../../43/users")]
    [InlineData("/entities/../../43/users")]
    [InlineData("/%2e%2e/%2e%2e/43/users")]
    [InlineData("\\..\\..\\43\\users")]
    public async Task Fetch_APathThatLeavesTheProject_IsRefused_AndNothingIsSent(string path)
    {
        var mock = new MockHttpMessageHandler();
        var any = AnyRequest(mock);

        var result = await Tool("fetch", CliToolScope.Local).RunAsync(Args(new { path }), Client(mock));

        result.ExitCode.Should().Be(1);
        mock.GetMatchCount(any).Should().Be(0);
    }

    [Theory]
    [InlineData("/entities", "/org/42/entities")]
    [InlineData("entities", "/org/42/entities")]
    [InlineData("/entities?page=2", "/org/42/entities")]
    public async Task Fetch_APathUnderTheProject_IsPrefixedWithTheProjectPath(string path, string expected)
    {
        var mock = new MockHttpMessageHandler();
        var sent = new List<Uri>();
        mock.When("*").Respond(req =>
        {
            sent.Add(req.RequestUri!);
            return new HttpResponseMessage { Content = new StringContent("[]") };
        });

        var result = await Tool("fetch", CliToolScope.Local).RunAsync(Args(new { path }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        sent.Should().ContainSingle().Which.AbsolutePath.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://attacker.example/x")]
    [InlineData("//attacker.example/x")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    public async Task Fetch_AnAbsoluteUrl_StaysOnTheProjectHost(string path)
    {
        var mock = new MockHttpMessageHandler();
        var sent = new List<Uri>();
        mock.When("*").Respond(req =>
        {
            sent.Add(req.RequestUri!);
            return new HttpResponseMessage { Content = new StringContent("[]") };
        });

        await Tool("fetch", CliToolScope.Local).RunAsync(Args(new { path }), Client(mock));

        sent.Should().ContainSingle().Which.Should().Match<Uri>(uri => uri.Host == "api.my.anythink.cloud" && uri.AbsolutePath.StartsWith("/org/42/"));
    }


    // ── Rule: true and false both reach a boolean option that can be set either way ──

    [Theory]
    [InlineData("entities_update", """{"name":"posts","public":true}""", "--public=true")]
    [InlineData("entities_update", """{"name":"posts","public":false}""", "--public=false")]
    [InlineData("entities_update", """{"name":"posts","rls":false}""", "--rls=false")]
    [InlineData("entities_update", """{"name":"posts","lock_records":false}""", "--lock-records=false")]
    [InlineData("fields_update", """{"entity":"posts","field_name":"title","required":false}""", "--required=false")]
    [InlineData("workflows_step_update", """{"workflow_id":1,"step_id":2,"enabled":false}""", "--enabled=false")]
    public void ABooleanThatCanBeSetEitherWay_IsPassedWithItsValue(string tool, string json, string expected)
    {
        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

        Tool(tool).BuildArgs(arguments).Should().Contain(expected);
    }

    [Theory]
    [InlineData(true, new[] { "--public" })]
    [InlineData(false, new string[0])]
    public void APlainFlag_IsPassedOnlyWhenTrue(bool value, string[] expected)
    {
        var args = Tool("entities_create").BuildArgs(Args(new { name = "posts", @public = value }));

        args.Should().Equal(new[] { "entities", "create", "posts" }.Concat(expected));
    }

    [Theory]
    [InlineData("""{"name":"posts","public":"false"}""")]
    [InlineData("""{"name":"posts","public":0}""")]
    public void ABooleanGivenSomethingElse_IsAToolError(string json)
    {
        var arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);

        var act = () => Tool("entities_update").BuildArgs(arguments);

        act.Should().Throw<ArgumentException>().WithMessage("*must be true or false*");
    }

    [Theory]
    [InlineData("public", "is_public")]
    [InlineData("rls", "enable_rls")]
    [InlineData("lock_records", "lock_new_records")]
    public async Task EntitiesUpdate_FalseIsSentAsFalse_AndTheRestIsKept(string option, string property)
    {
        JsonElement sent = default;
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{ApiUrl}/org/42/entities/posts").Respond("application/json",
            """{"name":"posts","table_name":"posts","enable_rls":true,"is_public":true,"lock_new_records":true,"fields":[]}""");
        mock.When(HttpMethod.Put, $"{ApiUrl}/org/42/entities/posts").Respond(async req =>
        {
            sent = JsonDocument.Parse(await req.Content!.ReadAsStringAsync()).RootElement.Clone();
            return new HttpResponseMessage { Content = new StringContent("""{"name":"posts","table_name":"posts","fields":[]}""") };
        });

        var result = await Tool("entities_update").RunAsync(
            new Dictionary<string, JsonElement> { ["name"] = Json("posts"), [option] = Json(false) }, Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        foreach (var name in new[] { "is_public", "enable_rls", "lock_new_records" })
            sent.GetProperty(name).GetBoolean().Should().Be(name != property, name);
    }

    [Fact]
    public async Task FieldsUpdate_FalseIsSentAsFalse()
    {
        JsonElement sent = default;
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{ApiUrl}/org/42/entities/posts/fields").Respond("application/json",
            """[{"id":9,"name":"title","database_type":"varchar","display_type":"input","is_required":true,"is_searchable":true,"publicly_searchable":true,"is_indexed":true}]""");
        mock.When(HttpMethod.Put, $"{ApiUrl}/org/42/entities/posts/fields/9").Respond(async req =>
        {
            sent = JsonDocument.Parse(await req.Content!.ReadAsStringAsync()).RootElement.Clone();
            return new HttpResponseMessage { Content = new StringContent("""{"id":9,"name":"title","database_type":"varchar","display_type":"input"}""") };
        });

        var result = await Tool("fields_update").RunAsync(Args(new { entity = "posts", field_name = "title", required = false }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        sent.GetProperty("is_required").GetBoolean().Should().BeFalse();
        sent.GetProperty("is_searchable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task WorkflowStepUpdate_FalseDisablesTheStep()
    {
        JsonElement sent = default;
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{ApiUrl}/org/42/workflows/1").Respond("application/json",
            """{"id":1,"name":"w","trigger":"Manual","enabled":true,"steps":[{"id":2,"name":"s","action":"log","enabled":true,"is_start_step":true}]}""");
        mock.When(HttpMethod.Put, $"{ApiUrl}/org/42/workflows/1/steps/2").Respond(async req =>
        {
            sent = JsonDocument.Parse(await req.Content!.ReadAsStringAsync()).RootElement.Clone();
            return new HttpResponseMessage { Content = new StringContent("""{"id":2,"name":"s","action":"log","enabled":false}""") };
        });

        var result = await Tool("workflows_step_update").RunAsync(Args(new { workflow_id = 1, step_id = 2, enabled = false }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        sent.GetProperty("enabled").GetBoolean().Should().BeFalse();
    }

    private static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    // ── Rule: a remote caller sees the status of an upstream failure, never its body ──

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task RemoteRun_UpstreamError_IsAToolErrorWithTheStatusButNotTheBody(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"{ApiUrl}/org/42/entities").Respond(System.Net.HttpStatusCode.InternalServerError, "application/json", """{"error":"secret-detail"}""");

        var result = await Tool("entities_list", scope).RunAsync(Args(new { }), Client(mock));

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("500").And.NotContain("secret-detail");
    }

    [Theory]
    [InlineData("entities_get", "GET", "/entities/posts", "not json")]
    [InlineData("entities_create", "POST", "/entities", "")]
    public async Task RemoteRun_AnUnreadableSuccessResponse_IsReportedAsSuch_NotAsAStatus(string tool, string method, string path, string body)
    {
        foreach (var scope in RemoteScopeList)
        {
            var mock = new MockHttpMessageHandler();
            mock.When(new HttpMethod(method), $"{ApiUrl}/org/42{path}").Respond("application/json", body);

            var result = await Tool(tool, scope).RunAsync(Args(new { name = "posts" }), Client(mock));

            result.ExitCode.Should().Be(1);
            result.Output.Should().Contain("response this command couldn't read").And.NotContain("status 200");
            if (body.Length > 0) result.Output.Should().NotContain(body);
        }
    }

    [Fact]
    public async Task LocalRun_UpstreamError_KeepsTheBody()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"{ApiUrl}/org/42/entities").Respond(System.Net.HttpStatusCode.BadRequest, "application/json", """{"error":"entity name taken"}""");

        var result = await Tool("entities_list", CliToolScope.Local).RunAsync(Args(new { }), Client(mock));

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("400").And.Contain("entity name taken");
    }

    // ── Rule: output is capped, with a hint to narrow the query ────────────────

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task Output_BeyondTheCap_IsCutOffWithAHint(CliToolScope scope)
    {
        var items = string.Join(',', Enumerable.Range(1, 4000).Select(i => $$"""{"id":{{i}},"title":"{{new string('x', 100)}}"}"""));
        var mock = new MockHttpMessageHandler();
        mock.When($"{ApiUrl}/org/42/entities/posts/items*").Respond("application/json",
            $$"""{"items":[{{items}}],"total_items":4000,"total_pages":1,"has_next_page":false,"page":1,"page_size":4000}""");

        var result = await Tool("data_list", scope).RunAsync(Args(new { entity = "posts" }), Client(mock));

        result.ExitCode.Should().Be(0);
        result.Output.Should().EndWith("to see the rest.]");
        result.Output.Length.Should().BeLessThan(CliRunner.MaxOutputCharacters + 300);
        result.Output.Should().Contain("Output cut off at 200,000 characters");
    }

    [Fact]
    public async Task Output_WithinTheCap_IsReturnedWhole()
    {
        var mock = new MockHttpMessageHandler();
        mock.When($"{ApiUrl}/org/42/entities").Respond("application/json", """[{"name":"posts","table_name":"posts","fields":[]}]""");

        var result = await Tool("entities_list").RunAsync(Args(new { }), Client(mock));

        result.Output.Should().NotContain("cut off");
    }

    [Fact]
    public void NoCliCommand_HasAParameterCalledProject()
    {
        CliCommandModel.All
            .SelectMany(command => command.Parameters.Select(parameter => $"{command.Key} {parameter.Name}"))
            .Where(parameter => parameter.EndsWith($" {HostedProjects.ParameterName}"))
            .Should().BeEmpty("hosted mode takes that name for its own project argument, and would swallow the command's value");
    }

    [Fact]
    public void OnlyHostedTools_TakeAProjectArgument()
    {
        foreach (var scope in new[] { CliToolScope.Local, CliToolScope.Internal })
            CliCommandTool.All(scope).Should().AllSatisfy(tool => Properties(tool).Should().NotContain(HostedProjects.ParameterName));
        CliCommandTool.All(CliToolScope.Hosted).Should().AllSatisfy(tool => Properties(tool).Should().Contain(HostedProjects.ParameterName));
    }

    [Fact]
    public async Task Cancelling_StopsTheCommandAtItsApiCall()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new HangingHandler();

        var run = Tool("data_list").RunAsync(
            Args(new { entity = "posts" }), new AnythinkClient("42", ApiUrl, new HttpClient(handler)), cancellation.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        cancellation.Cancel();
        var result = await run.WaitAsync(TimeSpan.FromSeconds(10));

        result.ExitCode.Should().Be(1);
        result.Output.Should().Be("Cancelled.");
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }
}
