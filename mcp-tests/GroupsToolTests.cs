using System.Text.Json;
using AnythinkCli.Client;
using AnythinkMcp.Cli;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

public class GroupsToolTests
{
    private static CliCommandTool Tool(string name, CliToolScope scope = CliToolScope.Internal) =>
        CliCommandTool.All(scope).Single(t => t.ProtocolTool.Name == name);

    private static Dictionary<string, JsonElement> Args(object values) =>
        JsonSerializer.SerializeToElement(values).EnumerateObject().ToDictionary(p => p.Name, p => p.Value);

    private static string ParameterDescription(string tool, string parameter) =>
        Tool(tool).ProtocolTool.InputSchema.GetProperty("properties").GetProperty(parameter).GetProperty("description").GetString()!;

    // Rule: a remote client can invite by email, so the address is free text rather than a plain path value.
    [Fact]
    public async Task GroupsInvite_Remotely_TakesAnEmailAddress()
    {
        string? body = null;
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, "https://api.my.anythink.cloud/org/42/groups/12/invites").Respond(async req =>
        {
            body = await req.Content!.ReadAsStringAsync();
            return new HttpResponseMessage
            {
                Content = new StringContent("""{"id":31,"group_id":12,"group_name":"U16","kind":"invite","status":"pending","role_id":4,"role":"Athlete","email":"sam+u16@example.com"}""")
            };
        });

        var result = await Tool("groups_invite").RunAsync(Args(new { group_id = 12, email = "sam+u16@example.com", role_id = 4 }),
            new AnythinkClient("42", "https://api.my.anythink.cloud", new HttpClient(mock)));

        result.ExitCode.Should().Be(0, result.Output);
        JsonDocument.Parse(body!).RootElement.GetProperty("email").GetString().Should().Be("sam+u16@example.com");
    }

    [Theory]
    [InlineData("groups_invites_approve", "invite_id", "groups_invites_list")]
    [InlineData("groups_members_update", "member_id", "groups_members_list")]
    [InlineData("groups_invite", "group_id", "groups_list")]
    [InlineData("groups_code", "role_id", "roles_list")]
    public void IdParameters_SayWhichToolListsThem(string tool, string parameter, string source) =>
        ParameterDescription(tool, parameter).Should().EndWith($"Find it with {source}.");

    // Joining, accepting and approving add a membership; declining, revoking and replacing a code take something away.
    [Theory]
    [InlineData("groups_join", false)]
    [InlineData("groups_accept", false)]
    [InlineData("groups_invites_accept", false)]
    [InlineData("groups_invites_approve", false)]
    [InlineData("groups_invites_decline", true)]
    [InlineData("groups_invites_revoke", true)]
    [InlineData("groups_code", true)]
    public void MembershipTools_AreMarkedDestructiveOnlyWhenTheyTakeSomethingAway(string tool, bool destructive) =>
        Tool(tool).ProtocolTool.Annotations!.DestructiveHint.Should().Be(destructive);

    [Fact]
    public void GroupsInvite_IsOpenWorld_BecauseItSendsAnEmail() =>
        Tool("groups_invite").ProtocolTool.Annotations!.OpenWorldHint.Should().BeTrue();
}
