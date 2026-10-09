using System.ComponentModel;
using System.Reflection;
using AnythinkMcp.Tools;
using FluentAssertions;

namespace AnythinkMcp.Tests;

public class CliToolDataImportDescriptionTests
{
    private static string Description() => typeof(CliTool).GetMethod(nameof(CliTool.RunCli))!
        .GetCustomAttribute<DescriptionAttribute>()!.Description;

    [Fact]
    public void CliTool_Description_TellsAgentsToDryRunImportsAndOnlyAddYesAfterApproval()
    {
        var text = Description();

        text.Should().Contain("data import").And.Contain("--dry-run");
        text.Should().Contain("only add '--yes' after the user approves");
    }

    [Fact]
    public void CliTool_DataImportAndExport_AreRefusedInHttpMode()
    {
        CliTool.HttpModeRefusal("data", ["import", "customers", "f.csv"]).Should().Contain("HTTP mode");
        CliTool.HttpModeRefusal("data", ["export", "customers", "f.csv"]).Should().Contain("HTTP mode");
        CliTool.HttpModeRefusal("data", ["list", "customers"]).Should().BeNull();
        CliTool.HttpModeRefusal("entities", ["import"]).Should().BeNull();
    }
}
