using System.Net;
using System.Text.Json.Nodes;
using AnythinkCli.Commands;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class UsersRolesCommandTests
{
    private const string Org = CommandRunner.Org;
    private const string User = """{"id":9,"first_name":"Ann","last_name":"Lee","email":"ann@example.com","is_confirmed":false,"created_at":"2026-01-01T00:00:00Z"}""";
    private const string Role = """{"id":5,"name":"editor","description":"d","is_active":true,"anyapi_access":false,"permissions":[{"id":10,"name":"posts:read","is_active":true}]}""";

    private static readonly UserInviteSettings Invite = new() { Email = "ann@example.com", FirstName = "Ann", LastName = "Lee" };

    [Fact]
    public async Task UsersInvite_SendsOneInvitation()
    {
        var mock = new MockHttpMessageHandler();
        var create = mock.ReplyWith(HttpMethod.Post, $"{Org}/users", User);
        var resend = mock.When(HttpMethod.Post, $"{Org}/users/9/send-invitation-email").Respond(HttpStatusCode.NoContent);

        var (code, _) = await CommandRunner.RunAsync(mock, () => new UsersInviteCommand().ExecuteAsync(null!, Invite));

        code.Should().Be(0);
        mock.GetMatchCount(create).Should().Be(1);
        mock.GetMatchCount(resend).Should().Be(0);
    }

    [Fact]
    public async Task UsersInvite_EmptySuccessResponse_IsSuccess()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{Org}/users").Respond(HttpStatusCode.NoContent);

        var (code, output) = await CommandRunner.RunAsync(mock, () => new UsersInviteCommand().ExecuteAsync(null!, Invite));

        code.Should().Be(0, output);
        output.Should().Contain("invitation sent");
    }

    [Fact]
    public async Task UsersResendInvite_CallsSendInvitationEmailOnce()
    {
        var mock = new MockHttpMessageHandler();
        var resend = mock.When(HttpMethod.Post, $"{Org}/users/9/send-invitation-email").Respond(HttpStatusCode.NoContent);

        var (code, _) = await CommandRunner.RunAsync(mock, () => new UsersResendInviteCommand().ExecuteAsync(null!, new UserResendInviteSettings { Id = 9 }));

        code.Should().Be(0);
        mock.GetMatchCount(resend).Should().Be(1);
    }

    [Fact]
    public async Task RolesCreate_SetsApiAccessByDefault()
    {
        var bodies = new List<string>();
        var mock = new MockHttpMessageHandler();
        mock.ReplyWith(HttpMethod.Post, $"{Org}/roles", Role, bodies);

        var (code, _) = await CommandRunner.RunAsync(mock, () => new RolesCreateCommand().ExecuteAsync(null!, new RoleCreateSettings { Name = "editor" }));

        code.Should().Be(0);
        JsonNode.Parse(bodies.Single())!["anyapi_access"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task RolesCreate_ApiAccessFalse_IsSent()
    {
        var bodies = new List<string>();
        var mock = new MockHttpMessageHandler();
        mock.ReplyWith(HttpMethod.Post, $"{Org}/roles", Role, bodies);

        await CommandRunner.RunAsync(mock, () => new RolesCreateCommand().ExecuteAsync(null!, new RoleCreateSettings { Name = "editor", ApiAccess = false }));

        JsonNode.Parse(bodies.Single())!["anyapi_access"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public async Task RolesUpdate_CanTurnApiAccessOn()
    {
        var bodies = new List<string>();
        var mock = new MockHttpMessageHandler();
        mock.ReplyWith(HttpMethod.Get, $"{Org}/roles/5", Role);
        mock.ReplyWith(HttpMethod.Put, $"{Org}/roles/5", Role, bodies);

        var (code, _) = await CommandRunner.RunAsync(mock, () => new RolesUpdateCommand().ExecuteAsync(null!, new RoleUpdateSettings { Id = 5, ApiAccess = true }));

        code.Should().Be(0);
        var put = JsonNode.Parse(bodies.Single())!;
        put["anyapi_access"]!.GetValue<bool>().Should().BeTrue();
        put["name"]!.GetValue<string>().Should().Be("editor");
        put["permission_ids"]!.AsArray().Select(n => n!.GetValue<int>()).Should().Equal(10);
    }

    [Fact]
    public async Task RolesUpdate_NameAndDescription_KeepApiAccess()
    {
        var bodies = new List<string>();
        var mock = new MockHttpMessageHandler();
        mock.ReplyWith(HttpMethod.Get, $"{Org}/roles/5", Role.Replace("false", "true"));
        mock.ReplyWith(HttpMethod.Put, $"{Org}/roles/5", Role, bodies);

        await CommandRunner.RunAsync(mock, () => new RolesUpdateCommand().ExecuteAsync(null!, new RoleUpdateSettings { Id = 5, Name = "writer", Description = "new" }));

        var put = JsonNode.Parse(bodies.Single())!;
        put["name"]!.GetValue<string>().Should().Be("writer");
        put["description"]!.GetValue<string>().Should().Be("new");
        put["anyapi_access"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task RolesUpdate_UnknownRole_Fails()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Get, $"{Org}/roles/5").Respond(HttpStatusCode.NoContent);

        var (code, _) = await CommandRunner.RunAsync(mock, () => new RolesUpdateCommand().ExecuteAsync(null!, new RoleUpdateSettings { Id = 5, ApiAccess = true }));

        code.Should().Be(1);
    }

    [Fact]
    public async Task RolesList_ShowsApiAccess()
    {
        var mock = new MockHttpMessageHandler();
        mock.ReplyWith(HttpMethod.Get, $"{Org}/roles", $"[{Role}]");

        var (_, output) = await CommandRunner.RunAsync(mock, () => new RolesListCommand().ExecuteAsync(null!, new EmptySettings()));

        output.Should().Contain("API access");
    }

    [Theory]
    [InlineData("users", "resend-invite")]
    [InlineData("roles", "update")]
    public void NewCommands_AreRegistered(string branch, string verb)
    {
        var app = new Spectre.Console.Cli.CommandApp();
        app.Configure(AnythinkCli.CliApp.Configure);
        app.Configure(c => c.Settings.Console = Spectre.Console.AnsiConsole.Create(new Spectre.Console.AnsiConsoleSettings { Out = new Spectre.Console.AnsiConsoleOutput(new StringWriter()) }));

        app.Run([branch, verb, "--help"]).Should().Be(0);
    }

    [Fact]
    public async Task UsersInvite_ApiRejects_Fails()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{Org}/users").Respond(HttpStatusCode.BadRequest, "text/plain", "no role");

        var (code, _) = await CommandRunner.RunAsync(mock, () => new UsersInviteCommand().ExecuteAsync(null!, Invite));

        code.Should().Be(1);
    }

    [Fact]
    public async Task RolesCreate_ApiRejects_Fails()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{Org}/roles").Respond(HttpStatusCode.BadRequest, "text/plain", "exists");

        var (code, _) = await CommandRunner.RunAsync(mock, () => new RolesCreateCommand().ExecuteAsync(null!, new RoleCreateSettings { Name = "editor" }));

        code.Should().Be(1);
    }
}
