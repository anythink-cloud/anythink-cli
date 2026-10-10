using System.Text.Json;
using AnythinkCli.Commands;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class RolesApiAccessTests
{
    private const string Role = """
        {"id":3,"name":"Editors","description":"Edit posts","is_active":true,"anyapi_access":false,
         "frontend_url":"https://app.example.com","permissions":[{"id":1,"name":"posts:read","entity_id":10,"is_active":true}]}
        """;

    private static MockHttpMessageHandler Api(List<string> sent)
    {
        var mock = new MockHttpMessageHandler();
        mock.ReplyWith(HttpMethod.Get, $"{CommandRunner.Org}/roles/3", Role);
        mock.ReplyWith(HttpMethod.Put, $"{CommandRunner.Org}/roles/3", Role, sent);
        mock.ReplyWith(HttpMethod.Post, $"{CommandRunner.Org}/roles", Role, sent);
        mock.ReplyWith(HttpMethod.Get, $"{CommandRunner.Org}/entities/posts", """{"id":10,"name":"posts","table_name":"default_posts","fields":[]}""");
        mock.ReplyWith(HttpMethod.Get, $"{CommandRunner.Org}/permissions",
            """[{"id":1,"name":"posts:read","entity_id":10,"is_active":true},{"id":2,"name":"posts:update","entity_id":10,"is_active":true}]""");
        return mock;
    }

    private static JsonElement Body(List<string> sent) => JsonDocument.Parse(sent.Single()).RootElement;

    [Fact]
    public async Task PermissionsAdd_KeepsTheRolesExternalApplicationUrl()
    {
        var sent = new List<string>();

        await CommandRunner.RunAsync(Api(sent), () => new RolesPermissionsAddCommand()
            .ExecuteAsync(null!, new RolePermissionsAddSettings { RoleId = 3, Entity = "posts", Actions = "update" }));

        Body(sent).GetProperty("frontend_url").GetString().Should().Be("https://app.example.com");
        Body(sent).GetProperty("permission_ids").EnumerateArray().Select(e => e.GetInt32()).Should().Equal(1, 2);
    }

    [Fact]
    public async Task PermissionsAdd_OnARoleWithApiAccessOff_SaysTheGrantHasNoEffectYet()
    {
        var (code, output) = await CommandRunner.RunAsync(Api([]), () => new RolesPermissionsAddCommand()
            .ExecuteAsync(null!, new RolePermissionsAddSettings { RoleId = 3, Entity = "posts", Actions = "update" }));

        code.Should().Be(0);
        output.Should().Contain("API access turned off").And.Contain("anythink roles update 3 --api-access");
    }

    [Fact]
    public async Task Create_WithApiAccess_TurnsItOn()
    {
        var sent = new List<string>();

        await CommandRunner.RunAsync(Api(sent), () => new RolesCreateCommand()
            .ExecuteAsync(null!, new RoleCreateSettings { Name = "Editors", ApiAccess = true }));

        Body(sent).GetProperty("anyapi_access").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Update_TurnsOnApiAccess_AndKeepsEverythingElse()
    {
        var sent = new List<string>();

        var (code, _) = await CommandRunner.RunAsync(Api(sent), () => new RolesUpdateCommand()
            .ExecuteAsync(null!, new RoleUpdateSettings { Id = 3, ApiAccess = true }));

        code.Should().Be(0);
        var body = Body(sent);
        body.GetProperty("anyapi_access").GetBoolean().Should().BeTrue();
        body.GetProperty("name").GetString().Should().Be("Editors");
        body.GetProperty("description").GetString().Should().Be("Edit posts");
        body.GetProperty("is_active").GetBoolean().Should().BeTrue();
        body.GetProperty("frontend_url").GetString().Should().Be("https://app.example.com");
        body.GetProperty("permission_ids").EnumerateArray().Select(e => e.GetInt32()).Should().Equal(1);
    }

    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, true, true)]
    public void Update_RefusesContradictoryFlags(bool apiAccess, bool noApiAccess, bool active, bool inactive) =>
        new RoleUpdateSettings { Id = 3, ApiAccess = apiAccess, NoApiAccess = noApiAccess, Active = active, Inactive = inactive }
            .Validate().Successful.Should().BeFalse();
}
