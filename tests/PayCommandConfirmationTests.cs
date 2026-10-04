using System.Net;
using AnythinkCli.Commands;
using AnythinkCli.Config;
using FluentAssertions;
using Spectre.Console.Cli;

namespace AnythinkCli.Tests;

// Rule: a destructive pay command run non-interactively without --yes exits non-zero and sends no write request.
[Collection("SequentialConfig")]
public class PayCommandConfirmationTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly HttpListener _listener = new();
    private readonly List<string> _requests = [];
    private readonly Guid _id = Guid.NewGuid();

    public PayCommandConfirmationTests()
    {
        Directory.CreateDirectory(_tempDir);
        ConfigService.ConfigDirOverride = _tempDir;
        var port = FreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _ = Task.Run(Serve);
        ConfigService.SaveProfile("t", new Profile { OrgId = "1", ApiKey = "k", InstanceApiUrl = $"http://127.0.0.1:{port}", Alias = "t" });
    }

    public void Dispose()
    {
        _listener.Close();
        ConfigService.ConfigDirOverride = null;
        Directory.Delete(_tempDir, recursive: true);
    }

    private static int FreePort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private async Task Serve()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); } catch { return; }
            lock (_requests) _requests.Add($"{ctx.Request.HttpMethod} {ctx.Request.Url!.AbsolutePath}");
            var body = System.Text.Encoding.UTF8.GetBytes("{}");
            ctx.Response.ContentType = "application/json";
            await ctx.Response.OutputStream.WriteAsync(body);
            ctx.Response.Close();
        }
    }

    private List<string> Writes()
    {
        lock (_requests) return _requests.Where(r => !r.StartsWith("GET ")).ToList();
    }

    private async Task AssertRefusedWithoutWriting(Task<int> run)
    {
        (await run).Should().NotBe(0);
        Writes().Should().BeEmpty();
    }

    [Fact]
    public Task PlansDelete_WithoutYes_RefusesAndSendsNoWrite()
        => AssertRefusedWithoutWriting(new PayPlansDeleteCommand().ExecuteAsync(null!, new PayPlansDeleteSettings { Id = 12 }));

    [Fact]
    public Task SubscriptionsCancel_WithoutYes_RefusesAndSendsNoWrite()
        => AssertRefusedWithoutWriting(new PaySubscriptionsCancelCommand().ExecuteAsync(null!, new PaySubscriptionsConfirmSettings { Id = _id.ToString() }));

    [Fact]
    public Task SubscriptionsDelete_WithoutYes_RefusesAndSendsNoWrite()
        => AssertRefusedWithoutWriting(new PaySubscriptionsDeleteCommand().ExecuteAsync(null!, new PaySubscriptionsConfirmSettings { Id = _id.ToString() }));

    [Fact]
    public Task SubscriptionsForceExpire_WithoutYes_RefusesAndSendsNoWrite()
        => AssertRefusedWithoutWriting(new PaySubscriptionsForceExpireCommand().ExecuteAsync(null!, new PaySubscriptionsConfirmSettings { Id = _id.ToString() }));

    [Fact]
    public Task SubscriptionsResync_WithoutYes_RefusesAndSendsNoWrite()
        => AssertRefusedWithoutWriting(new PaySubscriptionsResyncCommand().ExecuteAsync(null!, new PaySubscriptionsConfirmSettings { Id = _id.ToString() }));

    [Fact]
    public Task SubscriptionsRelink_WithoutYes_RefusesAndSendsNoWrite()
        => AssertRefusedWithoutWriting(new PaySubscriptionsRelinkCommand().ExecuteAsync(null!,
            new PaySubscriptionsRelinkSettings { Id = _id.ToString(), ToUserId = 4 }));

    [Fact]
    public async Task SubscriptionsCancel_WithYes_SendsTheWrite()
    {
        (await new PaySubscriptionsCancelCommand().ExecuteAsync(null!, new PaySubscriptionsConfirmSettings { Id = _id.ToString(), SkipConfirm = true })).Should().Be(0);

        Writes().Should().NotBeEmpty();
    }

    [Fact]
    public async Task PlansUpdate_ActiveWithInactive_IsRejectedBeforeAnyRequest()
    {
        var code = await new PayPlansUpdateCommand().ExecuteAsync(null!, new PayPlansUpdateSettings { Id = 1, Active = true, Inactive = true });

        code.Should().Be(1);
        lock (_requests) _requests.Should().BeEmpty();
    }
}
