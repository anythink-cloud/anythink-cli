using System.Reflection;
using System.Runtime.CompilerServices;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class ConsoleOutputCollectionTests
{
    [Fact]
    public void ConsoleOutputCollection_RunsAloneBecauseItSwapsTheGlobalConsole()
    {
        typeof(ConsoleOutputCollection).GetCustomAttribute<CollectionDefinitionAttribute>()!
            .DisableParallelization.Should().BeTrue();
    }

    [Fact]
    public void EveryTestFileThatUsesTheCommandRunner_IsInTheConsoleOutputCollection()
    {
        var usingRunner = Directory.GetFiles(TestDirectory(), "*.cs")
            .Where(f => Path.GetFileName(f) is not ("CommandRunner.cs" or "ConsoleOutputCollectionTests.cs"))
            .Where(f => File.ReadAllText(f).Contains("CommandRunner.RunAsync"))
            .Where(f => !File.ReadAllText(f).Contains("[Collection(\"ConsoleOutput\")]"))
            .Select(Path.GetFileName);

        usingRunner.Should().BeEmpty();
    }

    private static string TestDirectory([CallerFilePath] string thisFile = "") => Path.GetDirectoryName(thisFile)!;
}
