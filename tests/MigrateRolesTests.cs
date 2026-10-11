using System.Text.Json;
using AnythinkCli.Client;
using AnythinkCli.Commands;
using AnythinkCli.Config;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class MigrateRolesTests : IDisposable
{
    private const string Source = "https://source.example.com";
    private const string Target = "https://target.example.com";

    private readonly string _config = Directory.CreateTempSubdirectory("anythink-cfg-").FullName;

    public MigrateRolesTests()
    {
        ConfigService.ConfigDirOverride = _config;
        ConfigService.SaveProfile("source", new Profile { OrgId = "11111", ApiKey = "source-key", InstanceApiUrl = Source });
        ConfigService.SaveProfile("target", new Profile { OrgId = "22222", ApiKey = "target-key", InstanceApiUrl = Target });
    }

    public void Dispose()
    {
        ConfigService.ConfigDirOverride = null;
        Directory.Delete(_config, true);
    }

    private static AnythinkClient ClientFor(string url, string org, MockHttpMessageHandler mock) => new(org, url, new HttpClient(mock));

    [Fact]
    public async Task MigratingRoles_CopiesEachRolesExternalApplicationUrl()
    {
        var source = new MockHttpMessageHandler();
        source.ReplyWith(HttpMethod.Get, $"{Source}/org/11111/entities", "[]");
        source.ReplyWith(HttpMethod.Get, $"{Source}/org/11111/roles",
            """[{"id":3,"name":"Editors","description":"Edit posts","is_active":true,"anyapi_access":true,"frontend_url":"https://app.example.com","permissions":[]}]""");

        var sent = new List<string>();
        var target = new MockHttpMessageHandler();
        target.ReplyWith(HttpMethod.Get, $"{Target}/org/22222/entities", "[]");
        target.ReplyWith(HttpMethod.Get, $"{Target}/org/22222/roles", "[]");
        target.ReplyWith(HttpMethod.Get, $"{Target}/org/22222/permissions", "[]");
        target.ReplyWith(HttpMethod.Post, $"{Target}/org/22222/roles", """{"id":7,"name":"Editors","is_active":true}""");
        target.ReplyWith(HttpMethod.Put, $"{Target}/org/22222/roles/7", """{"id":7,"name":"Editors","is_active":true}""", sent);

        var clients = new Dictionary<string, AnythinkClient>
        {
            ["source"] = ClientFor(Source, "11111", source),
            ["target"] = ClientFor(Target, "22222", target),
        };

        var (code, output) = await CommandRunner.RunAsync(new MockHttpMessageHandler(), () =>
            new MigrateCommand { ClientForProfile = key => clients[key] }.ExecuteAsync(null!,
                new MigrateSettings { From = "source", To = "target", IncludeRoles = true }));

        code.Should().Be(0, output);
        var body = JsonDocument.Parse(sent.Single()).RootElement;
        body.GetProperty("frontend_url").GetString().Should().Be("https://app.example.com");
        body.GetProperty("name").GetString().Should().Be("Editors");
    }
}
