using System.Text.Json;
using AnythinkMcp.Cli;
using FluentAssertions;

namespace AnythinkMcp.Tests;

public class ImportDirectusToolTests
{
    private static CliCommandTool Import() =>
        CliCommandTool.All(CliToolScope.Local).Single(t => t.ProtocolTool.Name == "import_directus");

    private static Dictionary<string, JsonElement> Args(object value) =>
        JsonSerializer.SerializeToElement(value).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());

    // ── Rule: no tool can name a file to write, so it can never overwrite one of the user's own ──

    [Theory]
    [InlineData(CliToolScope.Local)]
    [InlineData(CliToolScope.Internal)]
    [InlineData(CliToolScope.Hosted)]
    public void ImportDirectus_Parameters_Exclude_Plan_And_Force(CliToolScope scope)
    {
        var parameters = CliToolPolicy.Parameters(CliCommandModel.All.Single(c => c.Key == "import directus"), scope).Select(p => p.Name).ToList();

        parameters.Should().NotContain(["plan", "force"]);
        if (scope == CliToolScope.Local) parameters.Should().Contain(["url", "dry_run"]);
    }

    [Fact]
    public void ImportDirectus_Generated_Args_Never_Contain_Plan_Or_Force()
    {
        var attempt = () => Import().BuildArgs(Args(new { url = "https://cms.example.com", dry_run = true, plan = "/home/me/.zshrc", force = true }));
        attempt.Should().Throw<ArgumentException>().WithMessage("*plan*");

        var args = Import().BuildArgs(Args(new { url = "https://cms.example.com", dry_run = true }));
        args.Should().NotContain(a => a.StartsWith("--plan") || a.StartsWith("--force"));
        args.Should().Contain("--dry-run").And.Contain("--json");
    }
}
