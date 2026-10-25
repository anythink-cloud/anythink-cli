using System.Text.Json.Nodes;
using Anythink.ProjectImporters.N8n;
using AnythinkCli.Client;
using AnythinkCli.Commands;
using FluentAssertions;
using RichardSzalay.MockHttp;
using static AnythinkCli.Tests.N8nFixtures;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class ImportN8nCommandTests
{
    private static ImportN8nCommand Command(StubHttpHandler n8n) =>
        new() { ImporterFactory = (_, _) => new N8nImporter(Base, new N8nClient(Base, KeyUnderTest, n8n)) };

    private static StubHttpHandler N8n() => new StubHttpHandler().On(ListPath, Page(OrderAlerts));

    private static MockHttpMessageHandler Project()
    {
        var project = new MockHttpMessageHandler();
        project.ReplyWith(HttpMethod.Get, $"{CommandRunner.Org}/entities", "[]");
        project.ReplyWith(HttpMethod.Get, $"{CommandRunner.Org}/workflows", "[]");
        return project;
    }

    // ── Rule: the key is read from the flag, then N8N_API_KEY, and the error names n8n ──

    [Fact]
    public void N8nImport_ApiKeyMissing_ErrorNamesN8nAndItsEnvironmentVariable()
    {
        var act = () => ImportDirectusCommand.ResolveToken(null, null, false, () => "typed", "n8n", ImportN8nCommand.TokenEnvVar);

        act.Should().Throw<CliException>().WithMessage("*n8n*N8N_API_KEY*");
    }

    [Fact]
    public void N8nImport_PlainHttpIsRefusedUnlessAllowed()
    {
        var act = () => ImportDirectusCommand.ValidateUrl("http://n8n.example.com", false);

        act.Should().Throw<CliException>().WithMessage("*http://*");
    }

    // ── Rule: --dry-run and --plan change nothing and print/save the plan ──

    [Fact]
    public async Task N8nImport_DryRunJson_PrintsOnlyThePlanAndWritesNothing()
    {
        var (code, output) = await CommandRunner.RunAsync(Project(), () => Command(N8n()).ExecuteAsync(null!,
            new ImportN8nSettings { Url = Base, Token = KeyUnderTest, DryRun = true, Json = true }));

        code.Should().Be(0, output);
        JsonNode.Parse(output)!["items"]!.AsArray().Should().Contain(i => i!["kind"]!.GetValue<string>() == "workflow");
        output.Should().NotContain(KeyUnderTest).And.NotContain(CredentialId);
    }

    [Fact]
    public async Task N8nImport_PlanFlag_SavesThePlanAndChangesNothing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"n8n-plan-{Guid.NewGuid():N}.json");
        try
        {
            var (code, output) = await CommandRunner.RunAsync(Project(), () => Command(N8n()).ExecuteAsync(null!,
                new ImportN8nSettings { Url = Base, Token = KeyUnderTest, Plan = path }));

            code.Should().Be(0, output);
            File.ReadAllText(path).Should().Contain("Order alerts").And.NotContain(KeyUnderTest);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task N8nImport_Apply_CreatesTheWorkflowDisabledAndExitsClean()
    {
        var bodies = new List<string>();
        var project = Project();
        project.ReplyWith(HttpMethod.Post, $"{CommandRunner.Org}/workflows", """{"id":40,"name":"Order alerts","trigger":"Api","enabled":false}""", bodies);
        project.ReplyWith(HttpMethod.Post, $"{CommandRunner.Org}/workflows/40/steps", """{"id":1,"key":"k","name":"n","action":"RunScript","enabled":true,"is_start_step":true}""");
        project.ReplyWith(HttpMethod.Put, $"{CommandRunner.Org}/workflows/40/steps/1", """{"id":1,"key":"k","name":"n","action":"RunScript","enabled":true,"is_start_step":true}""");

        var (code, output) = await CommandRunner.RunAsync(project, () => Command(N8n()).ExecuteAsync(null!,
            new ImportN8nSettings { Url = Base, Token = KeyUnderTest, Yes = true }));

        code.Should().Be(0, output);
        JsonNode.Parse(bodies.Single())!["enabled"]!.GetValue<bool>().Should().BeFalse();
        output.Should().NotContain(KeyUnderTest);
    }

    [Fact]
    public async Task N8nImport_ForceWithoutPlan_IsRefused()
    {
        var (code, output) = await CommandRunner.RunAsync(Project(), () => Command(N8n()).ExecuteAsync(null!,
            new ImportN8nSettings { Url = Base, Token = KeyUnderTest, Force = true }));

        code.Should().Be(1);
        output.Should().Contain("--plan");
    }
}
