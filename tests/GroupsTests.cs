using System.Net;
using AnythinkCli.Commands;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class GroupsTests
{
    private const string Org = CommandRunner.Org;

    private const string Member =
        """{"id":7,"group_id":12,"user_id":42,"role_id":4,"role":"Athlete","can_manage_group":false,"can_read_group_data":false,"user":{"id":42,"first_name":"Sam","last_name":"Lee","email":"sam@example.com"}}""";

    private const string Invite =
        """{"id":31,"group_id":12,"group_name":"Under 16s","kind":"invite","status":"pending","role_id":4,"role":"Athlete","email":"sam@example.com","expires_at":"2026-11-19T10:00:00Z"}""";

    private const string Code =
        """{"id":32,"group_id":12,"group_name":"Under 16s","kind":"code","status":"pending","role_id":4,"role":"Athlete","code":"K7QX2MPA"}""";

    private const string Request =
        """{"id":33,"group_id":12,"group_name":"Under 16s","kind":"request","status":"pending","role_id":4,"role":"Athlete","user":{"id":42,"first_name":"Sam","last_name":"Lee","email":"sam@example.com"}}""";

    private static Task<(int Code, string Output)> Run(MockHttpMessageHandler mock, Func<Task<int>> command) =>
        CommandRunner.RunAsync(mock, command);

    // ── Rule: each command calls the API path the server exposes, with the body it expects ──

    [Fact]
    public async Task Invite_PostsAnEmailInviteWithTheRole()
    {
        var sent = new List<string>();
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.ReplyWith(HttpMethod.Post, $"{Org}/groups/12/invites", Invite, sent);

        var (code, output) = await Run(mock, () => new GroupsInviteCommand().ExecuteAsync(null!,
            new GroupInviteSettings { GroupId = 12, Email = "sam@example.com", RoleId = 4 }));

        code.Should().Be(0);
        sent.Single().Should().Be("""{"kind":"invite","role_id":4,"email":"sam@example.com"}""");
        output.Should().Contain("Invite sent").And.Contain("sam@example.com").And.Contain("2026-11-19");
    }

    [Fact]
    public async Task Code_PostsACodeInvite_AndShowsTheCode()
    {
        var sent = new List<string>();
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.ReplyWith(HttpMethod.Post, $"{Org}/groups/12/invites", Code, sent);

        var (code, output) = await Run(mock, () => new GroupsCodeCommand().ExecuteAsync(null!, new GroupRoleSettings { GroupId = 12, RoleId = 4 }));

        code.Should().Be(0);
        sent.Single().Should().Be("""{"kind":"code","role_id":4}""");
        output.Should().Contain("K7QX2MPA");
    }

    [Fact]
    public void RoleCommands_RefuseToRunWithoutARole()
    {
        new GroupRoleSettings { GroupId = 12 }.Validate().Successful.Should().BeFalse();
        new GroupRoleSettings { GroupId = 12, RoleId = 4 }.Validate().Successful.Should().BeTrue();
    }

    [Fact]
    public async Task Join_WhenApprovalIsNeeded_SaysARequestWasSent()
    {
        var sent = new List<string>();
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.ReplyWith(HttpMethod.Post, $"{Org}/groups/join", $$"""{"status":"requested","request":{{Request}}}""", sent);

        var (code, output) = await Run(mock, () => new GroupsJoinCommand().ExecuteAsync(null!, new GroupJoinSettings { Code = "K7QX2MPA" }));

        code.Should().Be(0);
        sent.Single().Should().Be("""{"code":"K7QX2MPA"}""");
        output.Should().Contain("Request sent").And.Contain("request");
    }

    [Fact]
    public async Task Join_WhenApprovalIsOff_ShowsTheMembership()
    {
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.ReplyWith(HttpMethod.Post, $"{Org}/groups/join", $$"""{"status":"joined","member":{{Member}}}""");

        var (code, output) = await Run(mock, () => new GroupsJoinCommand().ExecuteAsync(null!, new GroupJoinSettings { Code = "K7QX2MPA" }));

        code.Should().Be(0);
        output.Should().Contain("Joined the group").And.Contain("Athlete (4)");
    }

    [Fact]
    public async Task Accept_PostsTheTokenToTheInviteAcceptPath()
    {
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.Expect(HttpMethod.Post, $"{Org}/group-invites/ABC123/accept").Respond("application/json", Member);

        var (code, _) = await Run(mock, () => new GroupsAcceptCommand().ExecuteAsync(null!, new GroupAcceptSettings { Token = "ABC123" }));

        code.Should().Be(0);
        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task MyInvites_AcceptRejectAndWithdraw_UseTheSignedInUsersPaths()
    {
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.Expect(HttpMethod.Post, $"{Org}/groups/invites/31/accept").Respond("application/json", Member);
        mock.Expect(HttpMethod.Post, $"{Org}/groups/invites/31/decline").Respond("application/json", Invite);
        mock.Expect(HttpMethod.Delete, $"{Org}/groups/invites/33").Respond("application/json", Request);

        (await Run(mock, () => new GroupsInvitesAcceptCommand().ExecuteAsync(null!, new MyInviteSettings { InviteId = 31 }))).Code.Should().Be(0);
        (await Run(mock, () => new GroupsInvitesRejectCommand().ExecuteAsync(null!, new MyInviteSettings { InviteId = 31 }))).Code.Should().Be(0);
        var (code, output) = await Run(mock, () => new GroupsInvitesWithdrawCommand().ExecuteAsync(null!, new MyInviteSettings { InviteId = 33 }));

        code.Should().Be(0);
        output.Should().Contain("withdrawn");
        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task ManagerDecisions_UseTheTeamsInvitePaths_AndApproveCarriesTheRole()
    {
        var sent = new List<string>();
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.ReplyWith(HttpMethod.Post, $"{Org}/groups/12/invites/33/approve", Member, sent);
        mock.Expect(HttpMethod.Post, $"{Org}/groups/12/invites/33/decline").Respond("application/json", Request);
        mock.Expect(HttpMethod.Delete, $"{Org}/groups/12/invites/31").Respond("application/json", Invite);

        (await Run(mock, () => new GroupsInvitesApproveCommand().ExecuteAsync(null!, new GroupRequestApproveSettings { GroupId = 12, InviteId = 33, RoleId = 5 }))).Code.Should().Be(0);
        (await Run(mock, () => new GroupsInvitesDeclineCommand().ExecuteAsync(null!, new GroupInviteIdSettings { GroupId = 12, InviteId = 33 }))).Code.Should().Be(0);
        var (code, output) = await Run(mock, () => new GroupsInvitesRevokeCommand().ExecuteAsync(null!, new GroupInviteRevokeSettings { GroupId = 12, InviteId = 31, Yes = true }));

        code.Should().Be(0);
        output.Should().Contain("revoked");
        sent.Single().Should().Be("""{"role_id":5}""");
        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task InvitesList_PassesTheStatusFilter_AndTablesEachKind()
    {
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.Expect(HttpMethod.Get, $"{Org}/groups/12/invites?status=pending").Respond("application/json", $"[{Invite},{Code},{Request}]");

        var (code, output) = await Run(mock, () => new GroupsInvitesListCommand().ExecuteAsync(null!,
            new GroupInvitesListSettings { GroupId = 12, Status = "pending" }));

        code.Should().Be(0);
        output.Should().Contain("sam@example.com").And.Contain("K7QX2MPA").And.Contain("Sam Lee");
        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task InvitesMine_CanPrintJson()
    {
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.ReplyWith(HttpMethod.Get, $"{Org}/groups/invites/mine", $"[{Invite}]");

        var (code, output) = await Run(mock, () => new GroupsInvitesMineCommand().ExecuteAsync(null!, new JsonSettings { Json = true }));

        code.Should().Be(0);
        output.Should().Contain("\"group_name\": \"Under 16s\"");
    }

    [Fact]
    public async Task InvitesMine_TablesInvites()
    {
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.ReplyWith(HttpMethod.Get, $"{Org}/groups/invites/mine", $"[{Invite}]");

        var (_, output) = await Run(mock, () => new GroupsInvitesMineCommand().ExecuteAsync(null!, new JsonSettings()));

        output.Should().Contain("Under 16s").And.Contain("pending");
    }

    // ── Rule: members ────────────────────────────────────────────────────────

    [Fact]
    public async Task MembersList_ShowsWhoCanManageAndReadData()
    {
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.ReplyWith(HttpMethod.Get, $"{Org}/groups/12/members", $"[{Member}]");

        var (code, output) = await Run(mock, () => new GroupsMembersListCommand().ExecuteAsync(null!, new GroupIdSettings { GroupId = 12 }));
        var (_, json) = await Run(mock, () => new GroupsMembersListCommand().ExecuteAsync(null!, new GroupIdSettings { GroupId = 12, Json = true }));

        code.Should().Be(0);
        output.Should().Contain("Sam Lee").And.Contain("Manages");
        json.Should().Contain("\"can_manage_group\": false");
    }

    [Fact]
    public async Task MembersAddUpdateRemove_SendTheRoleAndUseTheMemberPaths()
    {
        var sent = new List<string>();
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.ReplyWith(HttpMethod.Post, $"{Org}/groups/12/members", Member, sent);
        mock.ReplyWith(HttpMethod.Put, $"{Org}/groups/12/members/7", Member, sent);
        mock.Expect(HttpMethod.Delete, $"{Org}/groups/12/members/7").Respond(HttpStatusCode.NoContent);

        (await Run(mock, () => new GroupsMembersAddCommand().ExecuteAsync(null!, new GroupMemberAddSettings { GroupId = 12, UserId = 42, RoleId = 4 }))).Code.Should().Be(0);
        (await Run(mock, () => new GroupsMembersUpdateCommand().ExecuteAsync(null!, new GroupMemberUpdateSettings { GroupId = 12, MemberId = 7, RoleId = 5 }))).Code.Should().Be(0);
        var (code, output) = await Run(mock, () => new GroupsMembersRemoveCommand().ExecuteAsync(null!, new GroupMemberRemoveSettings { GroupId = 12, MemberId = 7, Yes = true }));

        code.Should().Be(0);
        output.Should().Contain("removed");
        sent.Should().Equal("""{"user_id":42,"role_id":4}""", """{"role_id":5}""");
        mock.VerifyNoOutstandingExpectation();
    }

    // ── Rule: groups themselves ──────────────────────────────────────────────

    [Fact]
    public async Task ListGetAndMine_ReadTheirPaths()
    {
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.ReplyWith(HttpMethod.Get, $"{Org}/groups", """[{"id":12,"name":"Under 16s","description":"Saturday"}]""");
        mock.ReplyWith(HttpMethod.Get, $"{Org}/groups/12", """{"id":12,"name":"Under 16s","owner_user_id":2}""");
        mock.ReplyWith(HttpMethod.Get, $"{Org}/groups/mine", """[{"group_id":12,"group_name":"Under 16s","role_id":5,"role":"Coach","can_manage_group":true}]""");

        (await Run(mock, () => new GroupsListCommand().ExecuteAsync(null!, new JsonSettings()))).Output.Should().Contain("Saturday");
        (await Run(mock, () => new GroupsListCommand().ExecuteAsync(null!, new JsonSettings { Json = true }))).Output.Should().Contain("\"name\": \"Under 16s\"");
        (await Run(mock, () => new GroupsGetCommand().ExecuteAsync(null!, new GroupIdSettings { GroupId = 12 }))).Output.Should().Contain("Owner user ID");
        (await Run(mock, () => new GroupsGetCommand().ExecuteAsync(null!, new GroupIdSettings { GroupId = 12, Json = true }))).Output.Should().Contain("\"owner_user_id\": 2");
        (await Run(mock, () => new GroupsMineCommand().ExecuteAsync(null!, new JsonSettings()))).Output.Should().Contain("Coach");
        (await Run(mock, () => new GroupsMineCommand().ExecuteAsync(null!, new JsonSettings { Json = true }))).Output.Should().Contain("\"can_manage_group\": true");
    }

    [Fact]
    public async Task Get_UnknownGroup_Fails()
    {
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.ReplyWith(HttpMethod.Get, $"{Org}/groups/99", "null");

        var (code, output) = await Run(mock, () => new GroupsGetCommand().ExecuteAsync(null!, new GroupIdSettings { GroupId = 99 }));

        code.Should().Be(1);
        output.Should().Contain("Group 99 not found");
    }

    [Fact]
    public async Task CreateUpdateDelete_SendOnlyTheValuesGiven()
    {
        var sent = new List<string>();
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.ReplyWith(HttpMethod.Post, $"{Org}/groups", """{"id":12,"name":"Under 16s"}""", sent);
        mock.ReplyWith(HttpMethod.Put, $"{Org}/groups/12", """{"id":12,"name":"Under 17s"}""", sent);
        mock.Expect(HttpMethod.Delete, $"{Org}/groups/12").Respond(HttpStatusCode.NoContent);

        (await Run(mock, () => new GroupsCreateCommand().ExecuteAsync(null!, new GroupCreateSettings { Name = "Under 16s", Description = "Saturday" }))).Code.Should().Be(0);
        (await Run(mock, () => new GroupsUpdateCommand().ExecuteAsync(null!, new GroupUpdateSettings { GroupId = 12, Name = "Under 17s" }))).Code.Should().Be(0);
        (await Run(mock, () => new GroupsDeleteCommand().ExecuteAsync(null!, new GroupConfirmSettings { GroupId = 12, Yes = true }))).Output.Should().Contain("deleted");

        sent.Should().Equal("""{"name":"Under 16s","description":"Saturday"}""", """{"name":"Under 17s"}""");
        mock.VerifyNoOutstandingExpectation();
    }

    [Fact]
    public async Task SettingsGet_ShowsJoinCodeApproval_DefaultingToRequired()
    {
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.ReplyWith(HttpMethod.Get, Org, """{"id":99999,"name":"Demo","tenant_settings":{"allow_registrations":false,"allowed_application_urls":[]}}""");

        var (code, output) = await Run(mock, () => new SettingsGetCommand().ExecuteAsync(null!, new SettingsGetSettings()));

        code.Should().Be(0);
        output.Should().MatchRegex(@"group_codes_require_approval\W+true");
    }

    [Fact]
    public async Task ARefusal_FromTheApi_FailsTheCommandWithItsMessage()
    {
        var mock = new MockHttpMessageHandler(BackendDefinitionBehavior.Always);
        mock.When(HttpMethod.Post, $"{Org}/groups/12/invites").Respond(HttpStatusCode.Forbidden, "text/plain", "Team managers cannot grant a project administrator role");

        var (code, output) = await Run(mock, () => new GroupsInviteCommand().ExecuteAsync(null!,
            new GroupInviteSettings { GroupId = 12, Email = "sam@example.com", RoleId = 1 }));

        code.Should().Be(1);
        output.Should().Contain("cannot grant a project administrator role");
    }
}
