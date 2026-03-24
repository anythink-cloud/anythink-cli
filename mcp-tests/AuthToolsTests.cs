using System.Net;
using AnythinkCli.Config;
using AnythinkMcp.Tools;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

[Collection("SequentialConfig")]
public class AuthToolsTests : McpTestBase
{
    // ── Signup ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Signup_Calls_Register_And_Saves_Platform()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{PlatformUrl}/org/{PlatformOrgId}/auth/v1/register")
            .Respond("application/json", """{}""");

        var factory = CreateFactory(mock);
        var tools = new AuthTools(factory);

        var result = await tools.Signup("Alice", "Smith", "alice@test.com", "P@ssw0rd!");

        result.Should().Contain("Account created");
        ConfigService.GetPlatform().Should().NotBeNull();
    }

    [Fact]
    public async Task Signup_With_Referral_Code()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{PlatformUrl}/org/{PlatformOrgId}/auth/v1/register")
            .Respond("application/json", """{}""");

        var factory = CreateFactory(mock);
        var tools = new AuthTools(factory);

        var result = await tools.Signup("Bob", "Jones", "bob@test.com", "P@ssw0rd!", "REF123");

        result.Should().Contain("Account created");
    }

    // ── Login ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_Saves_Platform_Token()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{PlatformUrl}/org/{PlatformOrgId}/auth/v1/token")
            .Respond("application/json", """
                {"access_token":"jwt-token-123","refresh_token":"rt-456","expires_in":3600}
            """);

        var factory = CreateFactory(mock);
        var tools = new AuthTools(factory);

        var result = await tools.Login("alice@test.com", "P@ssw0rd!");

        result.Should().Contain("Logged in successfully");
        var platform = ConfigService.GetPlatform()!;
        platform.Token.Should().Be("jwt-token-123");
        platform.TokenExpiresAt.Should().BeAfter(DateTime.UtcNow);
    }

    [Fact]
    public async Task Login_Failure_Throws()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{PlatformUrl}/org/{PlatformOrgId}/auth/v1/token")
            .Respond(HttpStatusCode.Unauthorized, "application/json", """
                "Invalid credentials"
            """);

        var factory = CreateFactory(mock);
        var tools = new AuthTools(factory);

        var act = () => tools.Login("wrong@test.com", "bad");

        await act.Should().ThrowAsync<Exception>();
    }

    // ── Login Direct ────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoginDirect_ApiKey_Saves_Profile()
    {
        var factory = CreateFactory(new MockHttpMessageHandler());
        var tools = new AuthTools(factory);

        var result = await tools.LoginDirect("99999", profile: "my-project", apiKey: "ak_test");

        result.Should().Contain("my-project");
        var profile = ConfigService.GetProfile("my-project");
        profile.Should().NotBeNull();
        profile!.OrgId.Should().Be("99999");
        profile.ApiKey.Should().Be("ak_test");
    }

    [Fact]
    public async Task LoginDirect_Token_Saves_Profile()
    {
        var factory = CreateFactory(new MockHttpMessageHandler());
        var tools = new AuthTools(factory);

        var result = await tools.LoginDirect("88888", token: "jwt-abc");

        result.Should().Contain("88888");
        var profile = ConfigService.GetProfile("88888");
        profile.Should().NotBeNull();
        profile!.AccessToken.Should().Be("jwt-abc");
    }

    [Fact]
    public async Task LoginDirect_CustomBaseUrl()
    {
        var factory = CreateFactory(new MockHttpMessageHandler());
        var tools = new AuthTools(factory);

        var result = await tools.LoginDirect("77777", apiKey: "ak_custom", baseUrl: "https://custom.api.com");

        var profile = ConfigService.GetProfile("77777")!;
        profile.InstanceApiUrl.Should().Be("https://custom.api.com");
    }

    [Fact]
    public async Task LoginDirect_No_Credentials_Returns_Error()
    {
        var factory = CreateFactory(new MockHttpMessageHandler());
        var tools = new AuthTools(factory);

        var result = await tools.LoginDirect("12345");

        result.Should().Contain("Error");
    }

    // ── Logout ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_Removes_Profile()
    {
        SetupProjectProfile("to-remove");
        var factory = CreateFactory(new MockHttpMessageHandler());
        var tools = new AuthTools(factory);

        var result = await tools.Logout("to-remove");

        result.Should().Contain("removed");
        ConfigService.GetProfile("to-remove").Should().BeNull();
    }

    [Fact]
    public async Task Logout_Default_Profile_Removes_Active()
    {
        SetupProjectProfile("active-one");
        var factory = CreateFactory(new MockHttpMessageHandler());
        var tools = new AuthTools(factory);

        var result = await tools.Logout();

        result.Should().Contain("removed");
        ConfigService.GetProfile("active-one").Should().BeNull();
    }

    [Fact]
    public async Task Logout_Unknown_Profile_Returns_Not_Found()
    {
        var factory = CreateFactory(new MockHttpMessageHandler());
        var tools = new AuthTools(factory);

        var result = await tools.Logout("nonexistent");

        result.Should().Contain("not found");
    }
}
