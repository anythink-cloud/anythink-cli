using AnythinkMcp.Cli;
using FluentAssertions;

namespace AnythinkMcp.Tests;

public class RemoteToolSurfaceTests
{
    private const string SnapshotFile = "RemoteToolSurface.txt";
    private const string HostedSnapshotFile = "HostedToolSurface.txt";

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
    public void HostedTools_MatchTheReviewedSnapshot() =>
        Surface(CliToolScope.Hosted).Should().Be(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, HostedSnapshotFile)),
            $"a CLI command or option changed what hosted clients can call; review it, then update mcp-tests/{HostedSnapshotFile}");

    [Fact]
    public void HostedTools_AreTheInternalToolsPlusAnOptionalProject() =>
        Surface(CliToolScope.Hosted).Should().Be(string.Join('\n', Surface().TrimEnd('\n').Split('\n').Select(line =>
        {
            var hint = line.IndexOf(']') + 1;
            var rest = line[hint..].TrimStart();
            return $"{line[..hint]} project?{(rest.Length > 0 ? " " + rest : "")}";
        })) + "\n");
}
