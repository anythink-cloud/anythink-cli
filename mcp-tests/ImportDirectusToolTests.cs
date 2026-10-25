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

    // ── Rule: the database password and service key never travel through a tool call ──

    [Fact]
    public void ImportSupabase_Parameters_Exclude_Secrets_Plan_And_Force()
    {
        var parameters = CliToolPolicy.Parameters(CliCommandModel.All.Single(c => c.Key == "import supabase"), CliToolScope.Local).Select(p => p.Name).ToList();

        parameters.Should().NotContain(["db", "service_key", "plan", "force"]);
        parameters.Should().Contain(["url", "schemas", "dry_run", "include_data", "include_files"]);
    }

    [Fact]
    public void ImportSupabase_Generated_Args_Refuse_A_Connection_String()
    {
        var tool = CliCommandTool.All(CliToolScope.Local).Single(t => t.ProtocolTool.Name == "import_supabase");

        var attempt = () => tool.BuildArgs(Args(new { db = "postgresql://postgres:pw@db.example.com/postgres", dry_run = true }));
        attempt.Should().Throw<ArgumentException>().WithMessage("*db*");

        var args = tool.BuildArgs(Args(new { dry_run = true }));
        args.Should().Contain("--dry-run").And.Contain("--json");
        args.Should().NotContain(a => a.StartsWith("--db") || a.StartsWith("--service-key"));
    }
}
