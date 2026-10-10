using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;

namespace AnythinkMcp.Tests;

public class McpStdioConformanceTests : IAsyncLifetime
{
    private Process? _process;
    private string? _tempHome;

    public Task InitializeAsync()
    {
        _tempHome = Directory.CreateTempSubdirectory("anythink-mcp-conformance-").FullName;

        var dllPath = FindServerDll();

        var psi = new ProcessStartInfo("dotnet", $"\"{dllPath}\"")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["HOME"] = _tempHome;
        psi.Environment["USERPROFILE"] = _tempHome;

        _process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start anythink-mcp.");
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(2000);
            }
        }
        catch { }
        finally
        {
            _process?.Dispose();
            if (_tempHome != null && Directory.Exists(_tempHome))
            {
                try { Directory.Delete(_tempHome, recursive: true); } catch { }
            }
        }
        return Task.CompletedTask;
    }

    private static string FindServerDll()
    {
        var dir = AppContext.BaseDirectory;
        var path = Path.Combine(dir, "anythink-mcp.dll");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Expected the MCP server build output at '{path}' (via the ProjectReference to mcp/AnythinkMcp.csproj). " +
                "Build tests-mcp before running this test.");
        return path;
    }

    private async Task SendAsync(object message)
    {
        var json = JsonSerializer.Serialize(message);
        await _process!.StandardInput.WriteLineAsync(json);
        await _process.StandardInput.FlushAsync();
    }

    private async Task<JsonDocument> ReceiveAsync(TimeSpan? timeout = null)
    {
        var readTask = _process!.StandardOutput.ReadLineAsync();
        var completed = await Task.WhenAny(readTask, Task.Delay(timeout ?? TimeSpan.FromSeconds(10)));
        if (completed != readTask)
        {
            var stderr = await TryReadStderrAsync();
            throw new TimeoutException($"anythink-mcp did not respond in time. Stderr so far:\n{stderr}");
        }
        var line = await readTask;
        if (string.IsNullOrWhiteSpace(line))
        {
            var stderr = await TryReadStderrAsync();
            throw new InvalidOperationException($"anythink-mcp produced no output. Stderr:\n{stderr}");
        }
        return JsonDocument.Parse(line);
    }

    private async Task<string> TryReadStderrAsync()
    {
        try
        {
            var task = _process!.StandardError.ReadToEndAsync();
            var completed = await Task.WhenAny(task, Task.Delay(500));
            return completed == task ? await task : "(stderr read timed out)";
        }
        catch { return "(stderr unavailable)"; }
    }


    [Fact]
    public async Task Handshake_ThenToolsList_ThenHarmlessToolCall_AllSucceed()
    {
        await SendAsync(new
        {
            jsonrpc = "2.0",
            id = 1,
            method = "initialize",
            @params = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new { },
                clientInfo = new { name = "ci-conformance-test", version = "1.0.0" }
            }
        });
        using (var initResponse = await ReceiveAsync())
        {
            var result = initResponse.RootElement.GetProperty("result");
            result.GetProperty("serverInfo").GetProperty("name").GetString().Should().Be("anythink");
            result.TryGetProperty("capabilities", out var caps).Should().BeTrue();
            caps.TryGetProperty("tools", out _).Should().BeTrue("the server must advertise tool support");
        }

        await SendAsync(new { jsonrpc = "2.0", method = "notifications/initialized" });

        await SendAsync(new { jsonrpc = "2.0", id = 2, method = "tools/list", @params = new { } });
        string[] toolNames;
        using (var toolsResponse = await ReceiveAsync())
        {
            var tools = toolsResponse.RootElement.GetProperty("result").GetProperty("tools");
            toolNames = tools.EnumerateArray().Select(t => t.GetProperty("name").GetString()!).ToArray();
        }

        toolNames.Should().Contain(new[]
        {
            "config_show", "config_use", "config_remove",
            "login", "login_direct", "login_google", "signup", "logout",
            "accounts_list", "accounts_use", "accounts_create",
            "projects_list", "projects_use", "projects_create", "projects_delete",
            "cli",
        }, "the tool catalog is the MCP client's only way to discover what it can call");

        await SendAsync(new
        {
            jsonrpc = "2.0",
            id = 3,
            method = "tools/call",
            @params = new { name = "config_show", arguments = new { } }
        });
        using var callResponse = await ReceiveAsync();
        var callResult = callResponse.RootElement.GetProperty("result");
        (callResult.TryGetProperty("isError", out var isError) && isError.GetBoolean()).Should().BeFalse(
            "a successful call either omits isError or sets it to false");

        var text = callResult.GetProperty("content")[0].GetProperty("text").GetString();
        text.Should().NotBeNullOrEmpty();
        using var configDoc = JsonDocument.Parse(text!);
        configDoc.RootElement.GetProperty("Profiles").GetArrayLength().Should().Be(0,
            "the temp HOME has no saved profiles");
    }
}
