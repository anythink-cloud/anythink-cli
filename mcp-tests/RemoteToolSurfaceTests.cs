using AnythinkMcp.Cli;
using FluentAssertions;

namespace AnythinkMcp.Tests;

public class RemoteToolSurfaceTests
{
    private const string SnapshotFile = "RemoteToolSurface.txt";
    private const string HostedSnapshotFile = "HostedToolSurface.txt";
    private const string LocalSnapshotFile = "LocalToolSurface.txt";

    internal static string Surface(CliToolScope scope = CliToolScope.Internal) => string.Join('\n', CliCommandTool.All(scope)
        .Select(tool =>
        {
            var annotations = tool.ProtocolTool.Annotations!;
            var hint = annotations.ReadOnlyHint == true ? "read-only" : annotations.DestructiveHint == true ? "destructive" : "write";
            if (annotations.OpenWorldHint == true) hint += ", open-world";
            var schema = tool.ProtocolTool.InputSchema;
            var required = schema.TryGetProperty("required", out var r) ? r.EnumerateArray().Select(e => e.GetString()).ToHashSet() : [];
            var parameters = schema.GetProperty("properties").EnumerateObject()
                .Select(p => required.Contains(p.Name) ? p.Name : p.Name + "?");
            return $"{tool.ProtocolTool.Name} [{hint}] {string.Join(' ', parameters)}".TrimEnd();
        })
        .Order(StringComparer.Ordinal)) + "\n";

    [Fact]
    public void RemoteTools_MatchTheReviewedSnapshot() =>
        Surface().Should().Be(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, SnapshotFile)),
            $"a CLI command or option changed what remote clients can call; review it, then update mcp-tests/{SnapshotFile}");

    [Fact]
    public void LocalTools_MatchTheReviewedSnapshot() =>
        Surface(CliToolScope.Local).Should().Be(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, LocalSnapshotFile)),
            $"a CLI command or option changed what local clients can call; review it, then update mcp-tests/{LocalSnapshotFile}");

    [Fact]
    public void HostedTools_MatchTheReviewedSnapshot() =>
        Surface(CliToolScope.Hosted).Should().Be(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, HostedSnapshotFile)),
            $"a CLI command or option changed what hosted clients can call; review it, then update mcp-tests/{HostedSnapshotFile}");

    private static bool IsAccountToolLine(string line) =>
        CliCommandTool.All(CliToolScope.Hosted).Where(t => t.NeedsAccountAccess).Any(t => line.StartsWith(t.ProtocolTool.Name + " ["));

    [Fact]
    public void HostedTools_AreTheInternalToolsPlusAnOptionalProject_AndTheAccountTools()
    {
        var hosted = Surface(CliToolScope.Hosted).TrimEnd('\n').Split('\n');

        string.Join('\n', hosted.Where(line => !IsAccountToolLine(line))).Should().Be(string.Join('\n', Surface().TrimEnd('\n').Split('\n').Select(line =>
        {
            var hint = line.IndexOf(']') + 1;
            var rest = line[hint..].TrimStart();
            return $"{line[..hint]} project?{(rest.Length > 0 ? " " + rest : "")}";
        })));
        hosted.Where(IsAccountToolLine).Should().HaveCount(5).And.AllSatisfy(line => line.Should().NotContain("project?"));
    }
}
