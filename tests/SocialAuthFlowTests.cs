using System.Net;
using System.Net.Sockets;
using System.Text;
using AnythinkCli.Auth;
using AnythinkCli.Config;
using FluentAssertions;

namespace AnythinkCli.Tests;

public class SocialAuthFlowTests
{
    [Theory]
    [InlineData("google", "Google")]
    [InlineData("microsoft", "Microsoft")]
    public void DisplayName_NamesTheProvider(string provider, string expected) =>
        SocialAuthFlow.DisplayName(provider).Should().Be(expected);

    [Fact]
    public async Task SigningIn_WithAProviderThatReturnsAnInsecureUrl_RefusesToOpenIt()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        var requested = "";
        using var server = new HttpListener();
        server.Prefixes.Add($"http://localhost:{port}/");
        server.Start();
        var serving = Task.Run(async () =>
        {
            var ctx = await server.GetContextAsync();
            requested = ctx.Request.Url!.AbsolutePath;
            var body = Encoding.UTF8.GetBytes("""{"authorization_url":"http://login.example.test/authorize"}""");
            await ctx.Response.OutputStream.WriteAsync(body);
            ctx.Response.Close();
        });

        var eff = new PlatformConfig { MyAnythinkUrl = $"http://localhost:{port}", MyAnythinkOrgId = "42" };
        var act = () => SocialAuthFlow.RunAsync(eff, "microsoft");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*non-https*");
        await serving;
        requested.Should().Be("/org/42/auth/v1/microsoft/authorize");
    }
}
