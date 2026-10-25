using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anythink.ProjectImporters;
using Anythink.ProjectImporters.Engine;
using Anythink.ProjectImporters.N8n;
using AnythinkCli.Client;
using FluentAssertions;
using static AnythinkCli.Tests.N8nFixtures;

namespace AnythinkCli.Tests;

public class N8nImporterTests
{
    private const string Org = "/org/99999";

    private static StubHttpHandler N8n(params string[] workflows) =>
        new StubHttpHandler().On(ListPath, Page(string.Join(",", workflows)));

    private static N8nImporter Importer(StubHttpHandler handler) =>
        new(Base, new N8nClient(Base, KeyUnderTest, handler));

    private static AnythinkClient Client(StubHttpHandler h) =>
        new("99999", "https://api.example.com", new HttpClient(h));

    private static StubHttpHandler EmptyProject() => new StubHttpHandler()
        .On($"{Org}/entities", "[]")
        .On($"{Org}/workflows", "[]")
        .On(HttpMethod.Post, $"{Org}/workflows", """{"id":40,"name":"x","trigger":"Api","enabled":false}""")
        .On(HttpMethod.Post, $"{Org}/workflows/40/steps", """{"id":1,"key":"k","name":"n","action":"RunScript","enabled":true,"is_start_step":true}""")
        .On(HttpMethod.Put, $"{Org}/workflows/40/steps/1", """{"id":1,"key":"k","name":"n","action":"RunScript","enabled":true,"is_start_step":true}""");

    private static async Task<ImportSchema> Schema(params string[] workflows) =>
        await Importer(N8n(workflows)).FetchSchemaAsync(includeFlows: true, includeFiles: false, includeRoles: false);

    private static ImportFlow Single(ImportSchema s) => s.Flows.Single();

    private static ImportStep Step(ImportFlow f, string name) => f.Steps.Single(s => s.Name == name);

    private static string Params(ImportStep s) => s.Parameters!.Value.GetRawText();

    // ── Rule: imported workflows are created disabled, whatever state they had in n8n ──

    [Fact]
    public async Task N8nImport_WorkflowsAreCreatedDisabled()
    {
        var target = EmptyProject();
        var runner = new ImportRunner(Importer(N8n(OrderAlerts)), Client(target), new ImportOptions(DryRun: false, IncludeFlows: true));

        var result = await runner.RunAsync();

        result.WorkflowsCreated.Should().Be(1);
        JsonNode.Parse(target.Matching(HttpMethod.Post, "/workflows").First(r => !r.PathAndQuery.Contains("steps")).Body)!["enabled"]!
            .GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task N8nImport_PlanStatesTheWorkflowIsCreatedDisabled()
    {
        var plan = await PlanFor(OrderAlerts);

        plan.Items.Single(i => i.Kind == PlanKind.Workflow).Spec!["enabled"]!.GetValue<bool>().Should().BeFalse();
    }

    // ── Rule: credentials are never read or imported; only their names and types are listed ──

    [Fact]
    public async Task N8nImport_CredentialsAreNeverRead()
    {
        var n8n = N8n(OrderAlerts);

        var schema = await Importer(n8n).FetchSchemaAsync(true, false, false);
        var plan = await PlanFor(schema);

        n8n.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get && r.PathAndQuery.StartsWith("/api/v1/workflows"));
        n8n.Requests.Should().NotContain(r => r.PathAndQuery.Contains("credential"));
        var everything = string.Join("\n", schema.Warnings!) + PlanJson.Serialise(plan) + JsonSerializer.Serialize(schema.Flows);
        everything.Should().NotContain(CredentialId).And.NotContain("cred-slack-1");
    }

    [Fact]
    public async Task N8nImport_CredentialsNeededAreListedAsConnectionsToSetUp()
    {
        var schema = await Schema(OrderAlerts);

        schema.Warnings.Should().ContainSingle(w =>
            w.Contains("needs connections set up") && w.Contains("Billing API key (httpHeaderAuth)") && w.Contains("Ops Slack (slackApi)"));
    }

    // ── Rule: the API key never appears in output, errors, plan files or logs ──

    [Fact]
    public async Task N8nImport_ApiKeyNeverAppearsInOutput()
    {
        var schema = await Schema(OrderAlerts);
        var plan = await PlanFor(schema);

        (PlanJson.Serialise(plan) + string.Join("\n", schema.Warnings!) + Importer(N8n()).ConnectionSummary)
            .Should().NotContain(KeyUnderTest);
        Importer(N8n()).SourceAuthToken.Should().BeNull();
    }

    [Fact]
    public async Task N8nImport_ApiKeyIsScrubbedFromErrors()
    {
        var n8n = new StubHttpHandler().On(ListPath, $"invalid key {KeyUnderTest} for user", HttpStatusCode.Unauthorized);

        var act = () => Importer(n8n).FetchSchemaAsync(true, false, false);

        var ex = (await act.Should().ThrowAsync<AnythinkException>()).Which;
        ex.Message.Should().NotContain(KeyUnderTest).And.Contain("***");
        ex.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task N8nImport_ApiKeyIsSentInTheHeaderNotTheUrl()
    {
        var capture = new CapturingHandler();

        await new N8nClient($"{Base}/", KeyUnderTest, capture).GetWorkflowsAsync();

        capture.Header.Should().Be(KeyUnderTest);
        capture.Url.Should().Be($"{Base}{ListPath}");
        capture.Url.Should().NotContain(KeyUnderTest);
    }

    [Fact]
    public void N8nImport_ConnectionSummaryRedactsUserinfoAndQuery()
    {
        new N8nImporter("https://user:pw@n8n.example.com/?apiKey=abc", KeyUnderTest).ConnectionSummary.Should().Be(Base);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Header { get; private set; }
        public string? Url { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Header = request.Headers.GetValues("X-N8N-API-KEY").Single();
            Url = request.RequestUri!.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Page("")) });
        }
    }

    // ── Rule: the source is read-only ──

    [Fact]
    public async Task N8nImport_NeverWritesToN8n()
    {
        var n8n = N8n(OrderAlerts);
        var runner = new ImportRunner(Importer(n8n), Client(EmptyProject()), new ImportOptions(DryRun: false, IncludeFlows: true));

        await runner.RunAsync();

        n8n.Requests.Should().OnlyContain(r => r.Method == HttpMethod.Get);
    }

    // ── Rule: access is never widened — only workflows are created ──

    [Fact]
    public async Task N8nImport_CreatesOnlyWorkflowsAndSteps()
    {
        var target = EmptyProject();
        var runner = new ImportRunner(Importer(N8n(OrderAlerts, NightlySync)), Client(target), new ImportOptions(DryRun: false, IncludeFlows: true));

        await runner.RunAsync();

        target.Writes.Should().OnlyContain(r => r.PathAndQuery.StartsWith($"{Org}/workflows"));
        var schema = await Schema(OrderAlerts);
        schema.Collections.Should().BeEmpty();
        schema.Roles.Should().BeEmpty();
        schema.Files.Should().BeEmpty();
    }

    // ── Rule: re-running is idempotent ──

    [Fact]
    public async Task N8nImport_RerunIsIdempotent()
    {
        var target = new StubHttpHandler()
            .On($"{Org}/entities", "[]")
            .On($"{Org}/workflows", """[{"id":3,"name":"Order alerts","trigger":"Api","enabled":true,"steps":[]}]""");
        var runner = new ImportRunner(Importer(N8n(OrderAlerts)), Client(target), new ImportOptions(DryRun: false, IncludeFlows: true));

        var result = await runner.RunAsync();

        target.Writes.Should().BeEmpty();
        result.WorkflowsSkipped.Should().Be(1);
        result.WorkflowsCreated.Should().Be(0);
    }

    [Fact]
    public async Task N8nImport_DuplicateWorkflowNames_AreKeptApartByTheirSourceId()
    {
        var a = Workflow("9", "Sync", Node("Webhook", "n8n-nodes-base.webhook", """{"path":"a"}"""));
        var b = Workflow("2", "Sync", Node("Webhook", "n8n-nodes-base.webhook", """{"path":"b"}"""));

        var schema = await Schema(a, b);

        schema.Flows.Select(f => f.Name).Should().Equal("Sync", "Sync (#9)");
    }

    // ── Rule: nodes with no Anythink equivalent are reported with name and type, never silently dropped ──

    [Fact]
    public async Task N8nImport_UnknownNode_IsReportedAsUnmapped()
    {
        var schema = await Schema(OrderAlerts);
        var plan = await PlanFor(schema);

        schema.Warnings.Should().Contain(w => w.Contains("'Notify Slack'") && w.Contains("n8n-nodes-base.slack") && w.Contains("no Anythink equivalent"));
        var placeholder = Step(Single(schema), "Notify Slack");
        placeholder.Enabled.Should().BeFalse();
        placeholder.NeedsManualReview.Should().BeTrue();
        plan.Items.Should().Contain(i => i.Kind == PlanKind.Report && i.Reason.Contains("Notify Slack") && i.Category == ReportCategory.NeedsReview);
    }

    [Fact]
    public async Task N8nImport_PythonCodeNode_IsReportedAsUnmapped()
    {
        var wf = Workflow("5", "Py", Node("Webhook", "n8n-nodes-base.webhook", """{"path":"p"}""") + "," +
                                      Node("Crunch", "n8n-nodes-base.code", """{"language":"python","pythonCode":"print(1)"}"""));

        var schema = await Schema(wf);

        schema.Warnings.Should().Contain(w => w.Contains("'Crunch'") && w.Contains("n8n-nodes-base.code"));
    }

    [Fact]
    public async Task N8nImport_StickyNotesAreAnnotationsNotSteps()
    {
        var schema = await Schema(OrderAlerts);

        Single(schema).Steps.Should().NotContain(s => s.Name == "Sticky Note");
        schema.Warnings.Should().NotContain(w => w.Contains("Sticky Note"));
    }

    [Theory]
    [InlineData("x\\nrequire('fs').rmSync('/')")]
    [InlineData("x\\u2028require('fs').rmSync('/')")]
    [InlineData("x\\u2029\\\"); require('fs').rmSync('/'); (\\\"")]
    public async Task N8nImport_UnmappedNodeName_CannotInjectCodeIntoItsPlaceholder(string name)
    {
        var wf = Workflow("6", "Evil", Node("Webhook", "n8n-nodes-base.webhook", """{"path":"e"}""") + "," +
                                       Node(name, "n8n-nodes-base.postgres"));

        var script = JsonDocument.Parse(Params(Single(await Schema(wf)).Steps.Single())).RootElement.GetProperty("script").GetString()!;

        script.Should().NotContain("\n").And.NotContain("\u2028").And.NotContain("\u2029");
        script.Should().StartWith("throw new Error(\"n8n node not imported: x").And.EndWith("\");");
    }

    // ── Rule: a node that was not imported stops the run instead of passing data through ──

    [Fact]
    public async Task N8nImport_UnmappedNode_PlaceholderThrowsSoAGateCannotBecomeAPassThrough()
    {
        var placeholder = Step(Single(await Schema(OrderAlerts)), "Notify Slack");

        JsonDocument.Parse(Params(placeholder)).RootElement.GetProperty("script").GetString()
            .Should().Be("throw new Error(\"n8n node not imported: Notify Slack (n8n-nodes-base.slack)\");");
    }

    [Fact]
    public async Task N8nImport_IfNode_FailsClosedUntilItsConditionsAreRewritten()
    {
        var p = JsonDocument.Parse(Params(Step(Single(await Schema(OrderAlerts)), "Is big order"))).RootElement;

        var conditions = p.GetProperty("filter_conditions");
        conditions.GetArrayLength().Should().BeGreaterThan(0);
        conditions[0].GetProperty("operator").GetString().Should().Be("eq");
        conditions[0].GetProperty("field").GetString().Should().NotBe(conditions[0].GetProperty("value").GetString());
        p.GetProperty("logical_operator").GetString().Should().Be("AND");
    }

    // ── Rule: triggers are sent disabled too, for every importer ──

    [Fact]
    public async Task Import_TriggersAreCreatedDisabled_N8n()
    {
        var target = EmptyProject();

        await new ImportRunner(Importer(N8n(OrderAlerts, NightlySync)), Client(target), new ImportOptions(DryRun: false, IncludeFlows: true)).RunAsync();

        var created = target.Matching(HttpMethod.Post, "/workflows").Where(r => !r.PathAndQuery.Contains("steps")).ToList();
        created.Should().HaveCount(2);
        foreach (var request in created)
        {
            var triggers = JsonNode.Parse(request.Body)!["triggers"]!.AsArray();
            triggers.Should().NotBeEmpty();
            triggers.Should().OnlyContain(t => t!["enabled"]!.GetValue<bool>() == false);
        }
    }

    [Fact]
    public async Task Import_TriggersAreCreatedDisabled_Directus()
    {
        const string cms = "https://cms.example.com";
        var source = new StubHttpHandler()
            .On("/collections?limit=-1", """{"data":[]}""").On("/fields?limit=-1", """{"data":[]}""").On("/relations?limit=-1", """{"data":[]}""")
            .On("/flows?limit=-1", """{"data":[{"id":"f1","name":"Nightly","status":"active","trigger":"schedule","options":{"cron":"0 1 * * *"},"operation":null}]}""")
            .On("/operations?limit=-1", """{"data":[]}""");
        var importer = new Anythink.ProjectImporters.Directus.DirectusImporter(cms,
            new Anythink.ProjectImporters.Directus.DirectusClient(cms, "tok", new HttpClient(source)));
        var target = EmptyProject();

        await new ImportRunner(importer, Client(target), new ImportOptions(DryRun: false, IncludeFlows: true)).RunAsync();

        var body = target.Matching(HttpMethod.Post, "/workflows").Single(r => !r.PathAndQuery.Contains("steps")).Body;
        JsonNode.Parse(body)!["triggers"]!.AsArray().Should().ContainSingle().Which!["enabled"]!.GetValue<bool>().Should().BeFalse();
    }

    // ── Rule: HTTP request settings with no Anythink equivalent are flagged, not silently dropped ──

    [Theory]
    [InlineData("""{"jsonParameters":true,"url":"https://x.example.com"}""")]
    [InlineData("""{"url":"https://x.example.com","bodyParametersJson":"{}"}""")]
    [InlineData("""{"url":"https://x.example.com","queryParametersJson":"{}"}""")]
    [InlineData("""{"url":"https://x.example.com","specifyHeaders":"json","jsonHeaders":"{}"}""")]
    [InlineData("""{"url":"https://x.example.com","specifyQuery":"json","jsonQuery":"{}"}""")]
    [InlineData("""{"url":"https://x.example.com","options":{"timeout":5}}""")]
    [InlineData("""{"url":"https://x.example.com","sendBody":true,"contentType":"multipart-form-data"}""")]
    [InlineData("""{"url":"https://x.example.com","sendBody":true,"contentType":"form-urlencoded"}""")]
    public async Task N8nImport_HttpRequestOptionsThatAreNotImported_AreFlaggedForReview(string parameters)
    {
        var wf = Workflow("18", "H", Node("Hook", "n8n-nodes-base.webhook", """{"path":"h"}""") + "," + Node("Call", "n8n-nodes-base.httpRequest", parameters));

        var step = Step(Single(await Schema(wf)), "Call");

        step.NeedsManualReview.Should().BeTrue();
        step.ReviewNote.Should().Contain("not imported");
    }

    [Fact]
    public async Task N8nImport_PlainHttpRequest_IsNotFlagged()
    {
        var wf = Workflow("19", "H", Node("Hook", "n8n-nodes-base.webhook", """{"path":"h"}""") + "," +
            Node("Call", "n8n-nodes-base.httpRequest", """{"url":"https://x.example.com","options":{}}"""));

        Step(Single(await Schema(wf)), "Call").NeedsManualReview.Should().BeFalse();
    }

    // ── Rule: secrets in Set values are replaced, and expressions are said to be literal text ──

    [Fact]
    public async Task N8nImport_SetNode_SecretLookingValuesArePlaceholderedAndFlagged()
    {
        var wf = Workflow("20", "S", Node("Hook", "n8n-nodes-base.webhook", """{"path":"s"}""") + "," +
            Node("Set", "n8n-nodes-base.set", """{"values":{"string":[{"name":"api_key","value":"sk_live_abcdef"},{"name":"plain","value":"ok"}]}}"""));

        var step = Step(Single(await Schema(wf)), "Set");

        Params(step).Should().NotContain("sk_live_abcdef").And.Contain("REPLACE_WITH_SECRET").And.Contain("ok");
        step.ReviewNote.Should().Contain("placeholder").And.Contain("literal text");
    }

    // ── Rule: the API key never follows a redirect to another host ──

    [Fact]
    public async Task N8nClient_NeverFollowsARedirect_SoTheKeyStaysWithTheSource()
    {
        using var other = new Listener(_ => (200, Page("")));
        using var source = new Listener(_ => (302, "")) { Location = other.Url + "/api/v1/workflows" };

        var act = () => new N8nClient(source.Url, KeyUnderTest).GetWorkflowsAsync();

        var ex = (await act.Should().ThrowAsync<AnythinkException>()).Which;
        ex.StatusCode.Should().Be(302);
        ex.Message.Should().Contain("127.0.0.1").And.NotContain("/api/v1").And.NotContain(KeyUnderTest);
        other.Hits.Should().Be(0);
        source.Keys.Should().ContainSingle().Which.Should().Be(KeyUnderTest);
    }

    private sealed class Listener : IDisposable
    {
        private readonly System.Net.HttpListener _listener = new();
        private readonly Func<System.Net.HttpListenerRequest, (int Status, string Body)> _reply;
        public string Url { get; }
        public string? Location { get; init; }
        public int Hits;
        public List<string?> Keys { get; } = [];

        public Listener(Func<System.Net.HttpListenerRequest, (int, string)> reply)
        {
            _reply = reply;
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            Url = $"http://127.0.0.1:{port}";
            _listener.Prefixes.Add(Url + "/");
            _listener.Start();
            _ = Task.Run(Loop);
        }

        private async Task Loop()
        {
            while (_listener.IsListening)
            {
                System.Net.HttpListenerContext ctx;
                try { ctx = await _listener.GetContextAsync(); } catch { return; }
                Interlocked.Increment(ref Hits);
                Keys.Add(ctx.Request.Headers["X-N8N-API-KEY"]);
                var (status, body) = _reply(ctx.Request);
                ctx.Response.StatusCode = status;
                if (Location is not null) ctx.Response.RedirectLocation = Location;
                var bytes = System.Text.Encoding.UTF8.GetBytes(body);
                await ctx.Response.OutputStream.WriteAsync(bytes);
                ctx.Response.Close();
            }
        }

        public void Dispose() => _listener.Close();
    }

    // ── Rule: triggers map to what Anythink supports ──

    [Theory]
    [InlineData("""{"field":"cronExpression","expression":"0 6 * * 1"}""", "0 6 * * 1")]
    [InlineData("""{"field":"minutes","minutesInterval":5}""", "*/5 * * * *")]
    [InlineData("""{"field":"minutes","minutesInterval":1}""", "* * * * *")]
    [InlineData("""{"field":"hours","hoursInterval":1,"triggerAtMinute":30}""", "30 * * * *")]
    [InlineData("""{"field":"days","daysInterval":1,"triggerAtHour":9,"triggerAtMinute":5}""", "5 9 * * *")]
    [InlineData("""{"field":"days","daysInterval":3,"triggerAtHour":9}""", "0 9 */3 * *")]
    [InlineData("""{"field":"weeks","weeksInterval":1,"triggerAtDay":[1,3],"triggerAtHour":8}""", "0 8 * * 1,3")]
    [InlineData("""{"field":"months","monthsInterval":1,"triggerAtDayOfMonth":15,"triggerAtHour":2}""", "0 2 15 * *")]
    public async Task N8nImport_ScheduleTrigger_BecomesATimedTriggerWithACronExpression(string interval, string cron)
    {
        var wf = Workflow("7", "S", Node("Tick", "n8n-nodes-base.scheduleTrigger", $$$"""{"rule":{"interval":[{{{interval}}}]}}"""));

        var trigger = Single(await Schema(wf)).Triggers.Single();

        trigger.Type.Should().Be("Timed");
        JsonSerializer.Serialize(trigger.Config).Should().Contain($"\"cron_expression\":\"{cron}\"");
    }

    [Theory]
    [InlineData("""{"mode":"everyMinute"}""", "* * * * *")]
    [InlineData("""{"mode":"everyHour","minute":10}""", "10 * * * *")]
    [InlineData("""{"mode":"everyDay","hour":7,"minute":30}""", "30 7 * * *")]
    [InlineData("""{"mode":"everyWeek","weekday":2,"hour":7}""", "0 7 * * 2")]
    [InlineData("""{"mode":"everyMonth","dayOfMonth":4,"hour":7}""", "0 7 4 * *")]
    [InlineData("""{"mode":"everyX","value":15,"unit":"minutes"}""", "*/15 * * * *")]
    [InlineData("""{"mode":"everyX","value":2,"unit":"hours"}""", "0 */2 * * *")]
    [InlineData("""{"mode":"everyX","value":1,"unit":"days"}""", "0 0 * * *")]
    public async Task N8nImport_CronNode_BecomesATimedTrigger(string item, string cron)
    {
        var wf = Workflow("8", "C", Node("Cron", "n8n-nodes-base.cron", $$$"""{"triggerTimes":{"item":[{{{item}}}]}}"""));

        JsonSerializer.Serialize(Single(await Schema(wf)).Triggers.Single().Config).Should().Contain($"\"cron_expression\":\"{cron}\"");
    }

    [Theory]
    [InlineData("""{"field":"seconds","secondsInterval":30}""")]
    [InlineData("""{"field":"weeks","weeksInterval":2,"triggerAtDay":[1]}""")]
    public async Task N8nImport_ScheduleThatAnythinkCannotExpress_IsReportedAndNotGuessed(string interval)
    {
        var wf = Workflow("9", "S", Node("Tick", "n8n-nodes-base.scheduleTrigger", $$$"""{"rule":{"interval":[{{{interval}}}]}}"""));

        var schema = await Schema(wf);

        Single(schema).Triggers.Single().Type.Should().Be("Manual");
        schema.Warnings.Should().Contain(w => w.Contains("'Tick'") && w.Contains("scheduleTrigger"));
    }

    [Fact]
    public async Task N8nImport_WebhookPath_BecomesAnApiRoute()
    {
        var trigger = Single(await Schema(OrderAlerts)).Triggers.Single();

        trigger.Type.Should().Be("Api");
        JsonSerializer.Serialize(trigger.Config).Should().Contain("\"api_route\":\"orders-new\"");
    }

    [Fact]
    public async Task N8nImport_WorkflowWithNoTranslatableTrigger_IsSkippedWithTheReasonInThePlan()
    {
        var wf = Workflow("10", "Mail", Node("New mail", "n8n-nodes-base.emailReadImapTrigger") + "," + Node("Log", "n8n-nodes-base.noOp"));

        var plan = await PlanFor(wf);

        plan.Items.Single(i => i.Kind == PlanKind.Workflow).Action.Should().Be(PlanAction.Skip);
        plan.Items.Single(i => i.Kind == PlanKind.Workflow).Reason.Should().Contain("manual_entities");
        plan.Items.Should().Contain(i => i.Kind == PlanKind.Report && i.Reason.Contains("'New mail'"));
    }

    [Fact]
    public async Task N8nImport_ManualTrigger_NeedsAnEntityAnythinkCannotInferSoTheWorkflowIsSkipped()
    {
        var wf = Workflow("11", "M", Node("Go", "n8n-nodes-base.manualTrigger") + "," + Node("Log", "n8n-nodes-base.noOp"),
            """{"Go":{"main":[[{"node":"Log","type":"main","index":0}]]}}""");

        var result = await new ImportRunner(Importer(N8n(wf)), Client(EmptyProject()), new ImportOptions(false, true)).RunAsync();

        result.WorkflowsSkipped.Should().Be(1);
        result.Warnings.Should().Contain(w => w.Contains("manual_entities"));
    }

    // ── Rule: the graph is wired like the original ──

    [Fact]
    public async Task N8nImport_IfNode_TrueBranchIsSuccessAndFalseBranchIsFailure()
    {
        var flow = Single(await Schema(OrderAlerts));

        var branch = Step(flow, "Is big order");
        branch.Action.Should().Be("Condition");
        branch.NeedsManualReview.Should().BeTrue();
        branch.OnSuccessSourceId.Should().Be("Notify Slack");
        branch.OnFailureSourceId.Should().Be("Do nothing");
    }

    [Fact]
    public async Task N8nImport_StartStep_IsTheNodeTheTriggerFeeds()
    {
        var flow = Single(await Schema(OrderAlerts));

        flow.Steps.Where(s => s.IsStartStep).Select(s => s.Name).Should().Equal("Fetch order");
        Step(flow, "Fetch order").OnSuccessSourceId.Should().Be("Shape payload");
    }

    [Fact]
    public async Task N8nImport_FanOut_KeepsTheFirstLinkAndSaysSo()
    {
        var wf = Workflow("12", "Fan", Node("Hook", "n8n-nodes-base.webhook", """{"path":"f"}""") + "," + Node("A", "n8n-nodes-base.noOp") + "," +
                                       Node("B", "n8n-nodes-base.noOp") + "," + Node("C", "n8n-nodes-base.noOp"),
            """{"Hook":{"main":[[{"node":"A"}]]},"A":{"main":[[{"node":"B"},{"node":"C"}]]}}""");

        var schema = await Schema(wf);

        Step(Single(schema), "A").OnSuccessSourceId.Should().Be("B");
        schema.Warnings.Should().Contain(w => w.Contains("'A'") && w.Contains("fans out"));
    }

    [Fact]
    public async Task N8nImport_DisabledNode_BecomesADisabledStep()
    {
        var wf = Workflow("13", "D", Node("Hook", "n8n-nodes-base.webhook", """{"path":"d"}""") + "," +
                                      Node("Off", "n8n-nodes-base.noOp", extra: ", \"disabled\": true"));

        Step(Single(await Schema(wf)), "Off").Enabled.Should().BeFalse();
    }

    // ── Rule: secrets in HTTP requests are replaced with a placeholder, not carried over ──

    [Fact]
    public async Task N8nImport_HttpRequest_SecretsInUrlAndHeadersArePlaceholdered()
    {
        var step = Step(Single(await Schema(OrderAlerts)), "Fetch order");

        step.Action.Should().Be("CallAnApi");
        var p = Params(step);
        p.Should().NotContain("abc123").And.NotContain("live-token-xyz");
        p.Should().Contain("REPLACE_WITH_SECRET").And.Contain("\"method\":\"GET\"").And.Contain("page=1");
        step.NeedsManualReview.Should().BeTrue();
        step.ReviewNote.Should().Contain("credential");
    }

    // ── Rule: n8n expressions can never resolve against the target project's template engine ──

    [Fact]
    public async Task N8nImport_Expressions_AreNeutralisedAndFlaggedForReview()
    {
        var wf = Workflow("14", "X", Node("Hook", "n8n-nodes-base.webhook", """{"path":"x"}""") + "," +
            Node("Steal", "n8n-nodes-base.httpRequest", """{"url":"={{ $anythink.secrets.stripe_key }}","method":"POST","jsonBody":"{\"v\":\"$anythink.secrets.k\"}"}"""));

        var step = Step(Single(await Schema(wf)), "Steal");

        Params(step).Should().NotContain("{{ $anythink").And.NotContain("{{$anythink").And.NotContain("\"$anythink");
        step.NeedsManualReview.Should().BeTrue();
    }

    [Fact]
    public async Task N8nImport_SetNode_BecomesAScriptReturningTheAssignedValues()
    {
        var step = Step(Single(await Schema(OrderAlerts)), "Shape payload");

        step.Action.Should().Be("RunScript");
        var script = JsonDocument.Parse(Params(step)).RootElement.GetProperty("script").GetString()!;
        script.Should().Contain("\"source\":\"n8n\"").And.Contain("\"retries\":3");
        script.Should().NotContain("{{ $json");
        step.NeedsManualReview.Should().BeTrue();
    }

    [Fact]
    public async Task N8nImport_LegacySetNode_ReadsTheValuesGroups()
    {
        var wf = Workflow("15", "L", Node("Hook", "n8n-nodes-base.webhook", """{"path":"l"}""") + "," +
            Node("Set", "n8n-nodes-base.set", """{"values":{"string":[{"name":"a","value":"b"}],"boolean":[{"name":"c","value":true}]}}"""));

        var script = JsonDocument.Parse(Params(Step(Single(await Schema(wf)), "Set"))).RootElement.GetProperty("script").GetString()!;

        script.Should().Contain("\"a\":\"b\"").And.Contain("\"c\":true");
    }

    [Fact]
    public async Task N8nImport_CodeNode_KeepsTheJavaScriptAndAsksForReview()
    {
        var step = Step(Single(await Schema(NightlySync)), "Run code");

        step.Action.Should().Be("RunScript");
        Params(step).Should().Contain("ok: true");
        step.NeedsManualReview.Should().BeTrue();
    }

    [Fact]
    public async Task N8nImport_EmailSend_BecomesSendAnEmail()
    {
        var wf = Workflow("16", "E", Node("Hook", "n8n-nodes-base.webhook", """{"path":"e"}""") + "," +
            Node("Mail", "n8n-nodes-base.emailSend", """{"toEmail":"ops@example.com","subject":"Hi","text":"Body"}"""));

        var step = Step(Single(await Schema(wf)), "Mail");

        step.Action.Should().Be("SendAnEmail");
        Params(step).Should().Contain("ops@example.com").And.Contain("Hi");
    }

    [Fact]
    public async Task N8nImport_HttpRequest_QueryAndBodyParametersAreCarriedOver()
    {
        var wf = Workflow("17", "H", Node("Hook", "n8n-nodes-base.webhook", """{"path":"h"}""") + "," +
            Node("Call", "n8n-nodes-base.httpRequest",
                """{"method":"POST","url":"https://x.example.com/a","queryParameters":{"parameters":[{"name":"q","value":"1 2"}]},"bodyParameters":{"parameters":[{"name":"n","value":"v"}]}}"""));

        var p = Params(Step(Single(await Schema(wf)), "Call"));

        p.Should().Contain("q=1%202").And.Contain("\\u0022n\\u0022:\\u0022v\\u0022");
    }

    // ── Rule: listing follows the cursor, and a hostile server cannot loop it forever ──

    [Fact]
    public async Task N8nImport_Listing_FollowsTheCursorUntilItEnds()
    {
        var n8n = new StubHttpHandler()
            .On(ListPath, Page(Workflow("1", "One", Node("Hook", "n8n-nodes-base.webhook", """{"path":"1"}""")), "abc def"))
            .On($"{ListPath}&cursor=abc%20def", Page(Workflow("2", "Two", Node("Hook", "n8n-nodes-base.webhook", """{"path":"2"}"""))));

        var schema = await Importer(n8n).FetchSchemaAsync(true, false, false);

        schema.Flows.Select(f => f.Name).Should().Equal("One", "Two");
    }

    [Fact]
    public async Task N8nImport_Listing_StopsWhenTheServerRepeatsACursor()
    {
        var n8n = new StubHttpHandler().On(ListPath, Page("", "same")).On($"{ListPath}&cursor=same", Page("", "same"));

        await Importer(n8n).FetchSchemaAsync(true, false, false);

        n8n.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task N8nImport_FlowsAreOnlyFetchedWhenRequested()
    {
        var n8n = N8n(OrderAlerts);

        var schema = await Importer(n8n).FetchSchemaAsync(includeFlows: false, false, false);

        schema.Flows.Should().BeEmpty();
        n8n.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task N8nImport_HasNoFilesOrRecordsToFetch()
    {
        var importer = Importer(N8n());

        (await importer.FetchRecordsAsync("x", 1, 10)).Records.Should().BeEmpty();
        var act = () => importer.GetFileDownloadUrl("1");
        act.Should().Throw<ImportException>();
    }

    private static Task<ImportPlan> PlanFor(string workflow) => PlanFor(Schema(workflow).Result);

    private static async Task<ImportPlan> PlanFor(ImportSchema schema) =>
        await new ImportRunner(new FixedSchemaImporter(schema), Client(EmptyProject()), new ImportOptions(DryRun: true, IncludeFlows: true))
            .BuildPlanAsync(schema);

    private sealed class FixedSchemaImporter(ImportSchema schema) : IPlatformImporter
    {
        public string PlatformName => "n8n";
        public string ConnectionSummary => Base;
        public string? SourceAuthToken => null;
        public Task<ImportSchema> FetchSchemaAsync(bool f, bool i, bool r) => Task.FromResult(schema);
        public Task<ImportRecordPage> FetchRecordsAsync(string c, int p, int s) => Task.FromResult(new ImportRecordPage([], 0));
        public string GetFileDownloadUrl(string id) => throw new NotSupportedException();
    }
}
