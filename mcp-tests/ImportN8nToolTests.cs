using System.Text.Json;
using AnythinkMcp.Cli;
using FluentAssertions;

namespace AnythinkMcp.Tests;

public class ImportN8nToolTests
{
    private static CliCommandTool Import(CliToolScope scope = CliToolScope.Local) =>
        CliCommandTool.All(scope).Single(t => t.ProtocolTool.Name == "import_n8n");

    private static Dictionary<string, JsonElement> Args(object value) =>
        JsonSerializer.SerializeToElement(value).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

    [Fact]
    public void ImportN8n_IsDestructive()
    {
        var annotations = Import().ProtocolTool.Annotations!;

        annotations.DestructiveHint.Should().BeTrue();
        annotations.ReadOnlyHint.Should().NotBe(true);
    }

    [Theory]
    [InlineData(CliToolScope.Internal)]
    [InlineData(CliToolScope.Hosted)]
    public void ImportN8n_ReadsFromAUserSuppliedServer_SoItIsLocalOnly(CliToolScope remote)
    {
        CliCommandTool.All(remote).Should().NotContain(t => t.ProtocolTool.Name == "import_n8n");
    }

    [Fact]
    public void ImportN8n_Parameters_ExcludePlanAndForce()
    {
        var parameters = CliToolPolicy.Parameters(CliCommandModel.All.Single(c => c.Key == "import n8n"), CliToolScope.Local).Select(p => p.Name).ToList();

        parameters.Should().NotContain(["plan", "force"]).And.Contain(["url", "dry_run"]);
    }

    [Fact]
    public void ImportN8n_Generated_Args_NeverContainPlanOrForce_AndAskForJson()
    {
        var attempt = () => Import().BuildArgs(Args(new { url = "https://n8n.example.com", dry_run = true, plan = "/home/me/.zshrc", force = true }));
        attempt.Should().Throw<ArgumentException>().WithMessage("*plan*");

        var args = Import().BuildArgs(Args(new { url = "https://n8n.example.com", dry_run = true }));
        args.Should().NotContain(a => a.StartsWith("--plan") || a.StartsWith("--force"));
        args.Should().Contain("--dry-run").And.Contain("--json");
    }
}
