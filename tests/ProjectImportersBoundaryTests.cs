using System.Reflection;
using Anythink.ProjectImporters;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class ProjectImportersBoundaryTests
{
    private static readonly Assembly Library = typeof(IImportTarget).Assembly;

    [Fact]
    public void ProjectImporters_DoesNotReferenceSpectreConsole()
    {
        Library.GetReferencedAssemblies().Select(a => a.Name)
            .Should().NotContain(n => n!.StartsWith("Spectre.Console"));
    }

    [Fact]
    public void ProjectImporters_DoesNotReferenceTheCli()
    {
        Library.GetReferencedAssemblies().Select(a => a.Name)
            .Should().NotContain(["anythink", "anythink-mcp"]);
    }

    [Fact]
    public void ProjectImporters_IsPackagedAsAnythinkProjectImporters()
    {
        Library.GetName().Name.Should().Be("Anythink.ProjectImporters");
    }

    [Fact]
    public void AnythinkClient_CanServeAsAnImportTarget()
    {
        typeof(IImportTarget).IsAssignableFrom(typeof(Client.AnythinkClient)).Should().BeTrue();
    }
}
