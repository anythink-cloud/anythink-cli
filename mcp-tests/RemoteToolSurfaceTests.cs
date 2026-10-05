using AnythinkMcp.Cli;
using FluentAssertions;

namespace AnythinkMcp.Tests;

public class RemoteToolSurfaceTests
{
    private const string SnapshotFile = "RemoteToolSurface.txt";

    internal static string Surface() => string.Join('\n', CliCommandTool.All(CliToolScope.Remote)
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
}
