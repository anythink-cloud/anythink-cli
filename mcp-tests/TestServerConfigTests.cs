using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;

namespace AnythinkMcp.Tests;

public class TestServerConfigTests
{
    [Fact]
    public void TestServers_DoNotWatchTheirConfigFiles()
    {
        var sources = ((IConfigurationBuilder)WebApplication.CreateBuilder().Configuration).Sources
            .OfType<JsonConfigurationSource>().ToList();

        sources.Should().NotBeEmpty();
        sources.Should().OnlyContain(source => !source.ReloadOnChange);
    }
}
