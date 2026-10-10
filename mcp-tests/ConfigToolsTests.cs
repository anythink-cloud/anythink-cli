using System.Text.Json;
using AnythinkCli.Config;
using AnythinkMcp.Tools;
using FluentAssertions;

namespace AnythinkMcp.Tests;

[Collection("SequentialConfig")]
public class ConfigToolsTests : McpTestBase
{
    [Fact]
    public async Task ConfigShow_Returns_All_Profiles()
    {
        SetupProjectProfile("proj-a", orgId: "111");
        SetupProjectProfile("proj-b", orgId: "222");

        var tools = new ConfigTools();

        var result = await tools.ConfigShow();
        var doc = JsonDocument.Parse(result);
        var profiles = doc.RootElement.GetProperty("Profiles");

        profiles.GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task ConfigShow_Shows_Active_Profile()
    {
        SetupProjectProfile("my-app");

        var tools = new ConfigTools();
        var result = await tools.ConfigShow();

        result.Should().Contain("my-app");
        var doc = JsonDocument.Parse(result);
        doc.RootElement.GetProperty("ActiveProfile").GetString().Should().Be("my-app");
    }

    [Fact]
    public async Task ConfigShow_Shows_Platform_Login_Status()
    {
        SetupPlatformLogin(accountId: "acct-123");

        var tools = new ConfigTools();
        var result = await tools.ConfigShow();

        var doc = JsonDocument.Parse(result);
        var platforms = doc.RootElement.GetProperty("Platforms");
        platforms.GetArrayLength().Should().BeGreaterThan(0);
        platforms[0].GetProperty("LoggedIn").GetBoolean().Should().BeTrue();
        platforms[0].GetProperty("AccountId").GetString().Should().Be("acct-123");
    }

    [Fact]
    public async Task ConfigUse_Switches_Active_Profile()
    {
        SetupProjectProfile("proj-a");
        SetupProjectProfile("proj-b");

        var tools = new ConfigTools();
        await tools.ConfigUse("proj-a");

        var config = ConfigService.Load();
        config.DefaultProfile.Should().Be("proj-a");
    }

    [Fact]
    public async Task ConfigUse_Unknown_Profile_Returns_Error()
    {
        var tools = new ConfigTools();

        var result = await tools.ConfigUse("nonexistent");

        result.Should().Contain("Error");
    }

    [Fact]
    public async Task ConfigRemove_Deletes_Profile()
    {
        SetupProjectProfile("to-delete");

        var tools = new ConfigTools();
        var result = await tools.ConfigRemove("to-delete");

        result.Should().Contain("removed");
        ConfigService.GetProfile("to-delete").Should().BeNull();
    }

    [Fact]
    public async Task ConfigRemove_Unknown_Profile_Returns_Not_Found()
    {
        var tools = new ConfigTools();

        var result = await tools.ConfigRemove("ghost");

        result.Should().Contain("not found");
    }
}
