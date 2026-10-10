using FluentAssertions;
using AnythinkMcp.Cli;

namespace AnythinkMcp.Tests;

public partial class CliCommandToolTests
{
    // ── Rule cluster: assistants may preview an import but never apply it ──

    [Fact]
    public void ImportCommands_AreAlwaysDryRunForAssistants()
    {
        var tool = Tool("import_directus", CliToolScope.Local);

        var plain = tool.BuildArgs(Args(new { url = "https://cms.example.com" }));
        plain.Should().Contain("--dry-run").And.NotContain("--yes");

        foreach (var attempt in new object[]
        {
            new { url = "https://cms.example.com", yes = true },
            new { url = "https://cms.example.com", dry_run = false },
            new { url = "https://cms.example.com", yes = true, dry_run = false }
        })
        {
            var build = () => tool.BuildArgs(Args(attempt));
            build.Should().Throw<ArgumentException>("the tool offers no way to apply an import");
        }

        Properties(tool).Should().NotContain(["yes", "dry_run"]);
    }

    [Fact]
    public void ImportCommands_AreAnnotatedReadOnly()
    {
        var tool = Tool("import_directus", CliToolScope.Local);

        tool.ProtocolTool.Annotations!.ReadOnlyHint.Should().BeTrue();
        tool.ProtocolTool.Annotations.DestructiveHint.Should().NotBe(true);
        tool.ProtocolTool.Description.Should().Contain("changes nothing").And.Contain("a person has to run the command themselves in a terminal");
    }

    [Theory]
    [InlineData(CliToolScope.Internal)]
    [InlineData(CliToolScope.Hosted)]
    public void ImportCommands_StayLocalOnly(CliToolScope remote)
    {
        Find("import_directus", remote).Should().BeNull();
        CliCommandTool.All(remote).Select(t => t.ProtocolTool.Name).Should().NotContain(name => name.StartsWith("import_"));
        Find("import_directus", CliToolScope.Local).Should().NotBeNull();
    }
}
