using AnythinkCli.Client;
using AnythinkCli.Commands;
using AnythinkCli.Config;
using FluentAssertions;
using Spectre.Console.Cli;

namespace AnythinkCli.Tests;

[Collection("SequentialConfig")]
public class RemoteScopeTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

    private sealed class TestCommand : BaseCommand<EmptyCommandSettings>
    {
        public override Task<int> ExecuteAsync(CommandContext ctx, EmptyCommandSettings s) => Task.FromResult(0);
        public AnythinkClient CallGetClient() => GetClient();
    }

    public RemoteScopeTests()
    {
        Directory.CreateDirectory(_tempDir);
        ConfigService.ConfigDirOverride = _tempDir;
        ConfigService.SaveProfile("server-login", new Profile
        {
            OrgId = "1",
            InstanceApiUrl = "https://api.example.com",
            ApiKey = "ak_the_servers_own_key"
        });
    }

    public void Dispose()
    {
        ClientContext.Remote = false;
        ClientContext.Current = null;
        ConfigService.ConfigDirOverride = null;
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    // ── Rule: a remote run never falls back to the server's own CLI login ──────

    [Fact]
    public void LocalRun_WithNoAmbientClient_UsesTheSavedProfile()
    {
        new TestCommand().CallGetClient().OrgId.Should().Be("1");
    }

    [Fact]
    public void RemoteRun_WithNoAmbientClient_IsRefused_EvenWithASavedProfile()
    {
        ClientContext.Remote = true;

        var act = () => new TestCommand().CallGetClient();

        act.Should().Throw<InvalidOperationException>().WithMessage("*aren't available to remote callers*");
    }

    [Fact]
    public void RemoteRun_WithAnAmbientClient_UsesIt()
    {
        var ambient = new AnythinkClient("42", "https://api.example.com", "token");
        ClientContext.Remote = true;
        ClientContext.Current = ambient;

        new TestCommand().CallGetClient().Should().BeSameAs(ambient);
    }

    [Fact]
    public void RemoteRun_CantReadOrWriteTheConfigFile()
    {
        ClientContext.Remote = true;

        Action[] attempts =
        [
            () => ConfigService.Load(),
            () => ConfigService.GetActiveProfile(),
            () => ConfigService.GetProfile("server-login"),
            () => ConfigService.SaveProfile("x", new Profile { OrgId = "1", InstanceApiUrl = "https://api.example.com" }),
            () => ConfigService.Save(new CliConfigData()),
            () => ConfigService.Reset(),
            () => ConfigService.ApplyRuntimeOverrides(new PlatformConfig()),
        ];

        foreach (var attempt in attempts)
            attempt.Should().Throw<InvalidOperationException>();
        ClientContext.Remote = false;
        ConfigService.GetProfile("server-login").Should().NotBeNull("the refused calls must not have touched the file");
    }

    [Fact]
    public async Task TheRemoteFlag_DoesNotLeakToACallerThatSetItInsideAnAsyncMethod()
    {
        static async Task SetRemote()
        {
            ClientContext.Remote = true;
            await Task.Yield();
        }

        await SetRemote();

        ClientContext.Remote.Should().BeFalse();
    }
}
