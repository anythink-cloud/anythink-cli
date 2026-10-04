using FluentAssertions;
using ModelContextProtocol;
using RichardSzalay.MockHttp;
using AnythinkMcp.Tools;

namespace AnythinkMcp.Tests;

public class HostedToolsTests
{
    private const string TenantUrl = "https://api.my.anythink.cloud/org/42";

    private readonly MockHttpMessageHandler _mock = new();

    private HostedTools Tools() => new(
        new McpClientFactory(null, _mock),
        new HostedCredentials { OrgId = "42", InstanceUrl = "https://api.my.anythink.cloud", Token = "t" });

    [Fact]
    public async Task ProjectDetails_ReturnsTheConnectedProject()
    {
        _mock.When(TenantUrl).Respond("application/json", """{"name":"Shop","description":"Store"}""");

        var details = await Tools().ProjectDetails();

        details.Should().Contain("\"org_id\":\"42\"").And.Contain("Shop").And.Contain("Store");
    }

    [Fact]
    public async Task UpstreamError_IsAToolError_WithTheStatusButNotTheBody()
    {
        _mock.When(TenantUrl)
            .Respond(System.Net.HttpStatusCode.InternalServerError, "application/json", """{"error":"secret-detail"}""");

        var act = () => Tools().ProjectDetails();

        (await act.Should().ThrowAsync<McpException>()).Which.Message
            .Should().Contain("500").And.NotContain("secret-detail");
    }
}
