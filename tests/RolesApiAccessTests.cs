using System.Net;
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

    private static MockHttpMessageHandler Api(List<string> sent, string role = Role)
    {
        var mock = new MockHttpMessageHandler();
        mock.ReplyWith(HttpMethod.Get, $"{CommandRunner.Org}/roles/3", role);
        mock.ReplyWith(HttpMethod.Put, $"{CommandRunner.Org}/roles/3", role, sent);
        mock.ReplyWith(HttpMethod.Post, $"{CommandRunner.Org}/roles", role, sent);
        mock.ReplyWith(HttpMethod.Get, $"{CommandRunner.Org}/entities/posts", """{"id":10,"name":"posts","table_name":"default_posts","fields":[]}""");
        mock.ReplyWith(HttpMethod.Get, $"{CommandRunner.Org}/permissions",
            """[{"id":1,"name":"posts:read","entity_id":10,"is_active":true},{"id":2,"name":"posts:update","entity_id":10,"is_active":true}]""");
        return mock;
    }

    private static JsonElement Body(List<string> sent) => JsonDocument.Parse(sent.Single()).RootElement;

    private static string WithApiAccessOn(string role) => role.Replace("\"anyapi_access\":false", "\"anyapi_access\":true");

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

    [Fact]
    public async Task PermissionsRemove_KeepsTheRolesExternalApplicationUrl()
    {
        var sent = new List<string>();

        var (code, _) = await CommandRunner.RunAsync(Api(sent), () => new RolesPermissionsRemoveCommand()
            .ExecuteAsync(null!, new RolePermissionsRemoveSettings { RoleId = 3, Entity = "posts", Actions = "read", Yes = true }));

        code.Should().Be(0);
        Body(sent).GetProperty("frontend_url").GetString().Should().Be("https://app.example.com");
        Body(sent).GetProperty("permission_ids").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public async Task PermissionsRemove_OnARoleWithApiAccessOff_SaysTheChangeHasNoEffectYet()
    {
        var (_, output) = await CommandRunner.RunAsync(Api([]), () => new RolesPermissionsRemoveCommand()
            .ExecuteAsync(null!, new RolePermissionsRemoveSettings { RoleId = 3, Entity = "posts", Actions = "read", Yes = true }));

        output.Should().Contain("API access turned off").And.Contain("anythink roles update 3 --api-access");
    }

    [Theory]
    [InlineData("""{"id":3,"name":"Editors","is_active":true,"anyapi_access":true,"permissions":[]}""")]
    [InlineData("""{"id":3,"name":"Admin User","is_active":true,"anyapi_access":false,"is_administrator":true,"permissions":[]}""")]
    public async Task PermissionsAdd_OnARoleWhoseUsersCanAlreadyUseTheApi_DoesNotWarn(string role)
    {
        var (code, output) = await CommandRunner.RunAsync(Api([], role), () => new RolesPermissionsAddCommand()
            .ExecuteAsync(null!, new RolePermissionsAddSettings { RoleId = 3, Entity = "posts", Actions = "read" }));

        code.Should().Be(0);
        output.Should().Contain("Added").And.NotContain("API access turned off");
    }

    [Fact]
    public async Task Create_WithoutApiAccess_LeavesItOff()
    {
        var sent = new List<string>();

        await CommandRunner.RunAsync(Api(sent), () => new RolesCreateCommand()
            .ExecuteAsync(null!, new RoleCreateSettings { Name = "Editors" }));

        Body(sent).GetProperty("anyapi_access").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Create_WhenTheApiRejectsTheRole_ExitsNonZero()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{CommandRunner.Org}/roles").Respond(HttpStatusCode.Conflict, "text/plain", "Role name already exists");

        var (code, output) = await CommandRunner.RunAsync(mock, () => new RolesCreateCommand()
            .ExecuteAsync(null!, new RoleCreateSettings { Name = "Editors" }));

        code.Should().Be(1);
        output.Should().Contain("Role name already exists");
    }

    [Fact]
    public async Task Update_ChangesOnlyWhatIsPassed_AndKeepsApiAccessAndPermissions()
    {
        var sent = new List<string>();

        var (code, _) = await CommandRunner.RunAsync(Api(sent), () => new RolesUpdateCommand().ExecuteAsync(null!,
            new RoleUpdateSettings { Id = 3, Name = "Writers", Description = "Write posts", FrontendUrl = "https://writers.example.com", Inactive = true }));

        code.Should().Be(0);
        var body = Body(sent);
        body.GetProperty("name").GetString().Should().Be("Writers");
        body.GetProperty("description").GetString().Should().Be("Write posts");
        body.GetProperty("frontend_url").GetString().Should().Be("https://writers.example.com");
        body.GetProperty("is_active").GetBoolean().Should().BeFalse();
        body.GetProperty("anyapi_access").GetBoolean().Should().BeFalse();
        body.GetProperty("permission_ids").EnumerateArray().Select(e => e.GetInt32()).Should().Equal(1);
    }

    [Fact]
    public async Task Update_NoApiAccess_TurnsItOffOnARoleThatHasIt()
    {
        var sent = new List<string>();

        await CommandRunner.RunAsync(Api(sent, WithApiAccessOn(Role)), () => new RolesUpdateCommand()
            .ExecuteAsync(null!, new RoleUpdateSettings { Id = 3, NoApiAccess = true }));

        Body(sent).GetProperty("anyapi_access").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Update_Active_TurnsAnInactiveRoleBackOn()
    {
        var sent = new List<string>();
        var inactive = Role.Replace("\"is_active\":true", "\"is_active\":false");

        await CommandRunner.RunAsync(Api(sent, inactive), () => new RolesUpdateCommand()
            .ExecuteAsync(null!, new RoleUpdateSettings { Id = 3, Active = true }));

        Body(sent).GetProperty("is_active").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Update_OfARoleThatDoesNotExist_ExitsNonZeroAndChangesNothing()
    {
        var sent = new List<string>();
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{CommandRunner.Org}/roles/3").Respond(HttpStatusCode.NotFound, "text/plain", "Role not found");
        mock.ReplyWith(HttpMethod.Put, $"{CommandRunner.Org}/roles/3", Role, sent);

        var (code, _) = await CommandRunner.RunAsync(mock, () => new RolesUpdateCommand()
            .ExecuteAsync(null!, new RoleUpdateSettings { Id = 3, ApiAccess = true }));

        code.Should().Be(1);
        sent.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, true, true)]
    public void Update_RefusesContradictoryFlags(bool apiAccess, bool noApiAccess, bool active, bool inactive) =>
        new RoleUpdateSettings { Id = 3, ApiAccess = apiAccess, NoApiAccess = noApiAccess, Active = active, Inactive = inactive }
            .Validate().Successful.Should().BeFalse();
}
