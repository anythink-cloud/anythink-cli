using FluentAssertions;

namespace AnythinkMcp.Tests;

/// <summary>
/// Tests for per-request credential isolation in HTTP mode.
/// </summary>
public class McpClientFactoryTests : IDisposable
{
    public McpClientFactoryTests()
    {
        // Ensure clean state before each test
        McpClientFactory.ClearRequestCredentials();
    }

    public void Dispose()
    {
        McpClientFactory.ClearRequestCredentials();
    }

    [Fact]
    public void GetRequestClient_WithoutRequestCredentials_Throws()
    {
        var act = () => new McpClientFactory().GetRequestClient();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ClearRequestCredentials_RemovesThem()
    {
        McpClientFactory.SetRequestCredentials("123", "https://api.example.com", "token");
        McpClientFactory.ClearRequestCredentials();

        var act = () => new McpClientFactory().GetRequestClient();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GetClient_ShouldUsePerRequestCredentials_InHttpMode()
    {
        McpClientFactory.SetRequestCredentials("42", "https://api.test.com", "my-token");
        var factory = new McpClientFactory();

        var client = factory.GetClient();

        client.OrgId.Should().Be("42");
        client.BaseUrl.Should().Be("https://api.test.com");
    }

    [Fact]
    public async Task PerRequestCredentials_ShouldBeIsolated_AcrossTasks()
    {
        // Simulate two concurrent requests with different credentials
        var task1OrgId = "";
        var task2OrgId = "";

        var task1 = Task.Run(() =>
        {
            McpClientFactory.SetRequestCredentials("1", "https://api1.com", "token1");
            Thread.Sleep(50); // Give task2 time to set its own credentials
            var factory = new McpClientFactory();
            task1OrgId = factory.GetClient().OrgId;
            McpClientFactory.ClearRequestCredentials();
        });

        var task2 = Task.Run(() =>
        {
            Thread.Sleep(10); // Start slightly after task1
            McpClientFactory.SetRequestCredentials("2", "https://api2.com", "token2");
            var factory = new McpClientFactory();
            task2OrgId = factory.GetClient().OrgId;
            McpClientFactory.ClearRequestCredentials();
        });

        await Task.WhenAll(task1, task2);

        task1OrgId.Should().Be("1", "task1 should see its own credentials");
        task2OrgId.Should().Be("2", "task2 should see its own credentials");
    }
}
