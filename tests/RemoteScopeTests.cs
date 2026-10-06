using AnythinkCli.Client;
using AnythinkCli.Commands;
using AnythinkCli.Config;
using FluentAssertions;
using RichardSzalay.MockHttp;
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

    private sealed class BillingCommand : BasePlatformCommand<EmptyCommandSettings>
    {
        public override Task<int> ExecuteAsync(CommandContext ctx, EmptyCommandSettings s) => Task.FromResult(0);
        public BillingClient CallGetBillingClient() => GetBillingClient();
        public BillingClient CallGetUnauthenticatedBillingClient() => GetUnauthenticatedBillingClient();
    }

    public RemoteScopeTests()
    {
        Directory.CreateDirectory(_tempDir);
        ConfigService.ConfigDirOverride = _tempDir;
        ConfigService.SaveProfile("server-login", new Profile
        {
            OrgId = "1", InstanceApiUrl = "https://api.example.com", ApiKey = "ak_the_servers_own_key"
        });
    }

    public void Dispose()
    {
        ClientContext.Remote = false;
        ClientContext.Current = null;
        ClientContext.Billing = null;
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

    // ── Rule: a remote run never bills through the server's saved platform login ──

    private static void SaveAPlatformLogin() =>
        ConfigService.SavePlatform(new PlatformConfig { Token = "the-servers-own-platform-token", TokenExpiresAt = DateTime.UtcNow.AddHours(1) });

    [Fact]
    public void LocalRun_WithASavedPlatformLogin_UsesItForBilling()
    {
        SaveAPlatformLogin();

        var act = () => new BillingCommand().CallGetBillingClient();

        act.Should().NotThrow();
    }

    [Fact]
    public void RemoteRun_WithNoAmbientBillingClient_IsRefused_EvenWithASavedPlatformLogin()
    {
        SaveAPlatformLogin();
        ClientContext.Remote = true;

        new BillingCommand().Invoking(c => c.CallGetBillingClient())
            .Should().Throw<InvalidOperationException>().WithMessage("*aren't available to remote callers*");
        new BillingCommand().Invoking(c => c.CallGetUnauthenticatedBillingClient())
            .Should().Throw<InvalidOperationException>().WithMessage("*aren't available to remote callers*");
    }

    [Fact]
    public async Task RemoteRun_WithAnAmbientBillingClient_UsesItForBilling_AndItsAnonymousTwinForPlans()
    {
        SaveAPlatformLogin();
        var mock = new MockHttpMessageHandler();
        var signedIn = mock.When("https://billing.example/v1/plans").WithHeaders("Authorization", "Bearer callers-token").Respond("application/json", "[]");
        var anonymous = mock.When("https://billing.example/v1/plans").With(request => request.Headers.Authorization is null).Respond("application/json", "[]");
        var authenticated = new HttpClient(mock);
        authenticated.DefaultRequestHeaders.Authorization = new("Bearer", "callers-token");
        var ambient = new BillingClient("https://billing.example", authenticated, new HttpClient(mock));
        ClientContext.Remote = true;
        ClientContext.Billing = ambient;

        new BillingCommand().CallGetBillingClient().Should().BeSameAs(ambient);
        await new BillingCommand().CallGetUnauthenticatedBillingClient().GetPlansAsync();

        mock.GetMatchCount(anonymous).Should().Be(1);
        mock.GetMatchCount(signedIn).Should().Be(0);
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
