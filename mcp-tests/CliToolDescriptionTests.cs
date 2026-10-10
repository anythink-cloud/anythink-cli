using System.ComponentModel;
using System.Reflection;
using AnythinkMcp.Tools;
using FluentAssertions;

namespace AnythinkMcp.Tests;

public class CliToolDescriptionTests
{
    [Fact]
    public void Cli_Tool_Description_Tells_Agents_To_Dry_Run_Directus_Imports_First()
    {
        var description = typeof(CliTool).GetMethod(nameof(CliTool.RunCli))!
            .GetCustomAttribute<DescriptionAttribute>()!.Description;

        description.Should().Contain("import directus").And.Contain("--dry-run").And.Contain("--yes");
    }
}
