using System.Net;
using AnythinkCli.Commands;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
public class NoContentResponsesTests
{
    private static async Task<(int Code, string Output)> RunAgainstNoContent(string path, Func<Task<int>> command)
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, $"{CommandRunner.Org}{path}").Respond(HttpStatusCode.NoContent);
        mock.ReplyWith(HttpMethod.Post, $"{CommandRunner.Org}/users",
            """{"id":8,"first_name":"Ada","last_name":"Lovelace","email":"ada@example.com"}""");
        return await CommandRunner.RunAsync(mock, command);
    }

    [Fact]
    public async Task WorkflowsDisable_WhenServerReturnsNoContent_ReportsSuccess()
    {
        var (code, output) = await RunAgainstNoContent("/workflows/150/disable",
            () => new WorkflowsDisableCommand().ExecuteAsync(null!, new WorkflowIdSettings { Id = 150 }));

        code.Should().Be(0);
        output.Should().Contain("Workflow 150 disabled.");
    }

    [Fact]
    public async Task WorkflowsEnable_WhenServerReturnsNoContent_ReportsSuccess()
    {
        var (code, output) = await RunAgainstNoContent("/workflows/150/enable",
            () => new WorkflowsEnableCommand().ExecuteAsync(null!, new WorkflowIdSettings { Id = 150 }));

        code.Should().Be(0);
        output.Should().Contain("Workflow 150 enabled.");
    }

    [Fact]
    public async Task UsersInvite_WhenInvitationEmailReturnsNoContent_ReportsSuccess()
    {
        var (code, output) = await RunAgainstNoContent("/users/8/send-invitation-email",
            () => new UsersInviteCommand().ExecuteAsync(null!,
                new UserInviteSettings { Email = "ada@example.com", FirstName = "Ada", LastName = "Lovelace", RoleId = 3 }));

        code.Should().Be(0);
        output.Should().Contain("invitation sent");
    }
}
