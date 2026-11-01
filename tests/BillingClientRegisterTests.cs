using AnythinkCli.Client;
using AnythinkCli.Config;
using AnythinkCli.Models;
using FluentAssertions;
using RichardSzalay.MockHttp;
using System.Net;

namespace AnythinkCli.Tests;

public class BillingClientRegisterTests
{
    private const string RegisterUrl = "https://api.example.com/org/1/auth/v1/register";

    private static BillingClient Build(MockHttpMessageHandler mock) => new(
        new PlatformConfig { MyAnythinkUrl = "https://api.example.com", BillingUrl = "https://billing.example.com", MyAnythinkOrgId = "1" },
        mock.ToHttpClient());

    private static readonly RegisterRequest Request = new("Ada", "Lovelace", "ada@example.com", "pw", null);

    [Fact]
    public async Task Register_WhenTheApiAnswersInPlainText_Succeeds()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RegisterUrl).Respond("text/plain", "Registration successful");

        await Build(mock).Invoking(client => client.RegisterAsync(Request)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task Register_WhenTheApiRefuses_ReportsTheFailure()
    {
        var mock = new MockHttpMessageHandler();
        mock.When(HttpMethod.Post, RegisterUrl).Respond(HttpStatusCode.BadRequest, "text/plain", "Organisation does not allow registrations");

        await Build(mock).Invoking(client => client.RegisterAsync(Request)).Should()
            .ThrowAsync<AnythinkException>().Where(e => e.StatusCode == 400);
    }
}
