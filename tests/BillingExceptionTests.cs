using AnythinkCli.Client;
using AnythinkCli.Config;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

public class BillingExceptionTests
{
    private static string Remote(int status, string body) => new BillingException(body, status).StatusOnlyMessage;

    // ── Rule: a billing 400 explains itself to a remote caller; everything else is status only ──

    [Theory]
    [InlineData("""{"error":"A paid plan needs a payment method."}""", "A paid plan needs a payment method.")]
    [InlineData("\"A paid plan needs a payment method.\"", "A paid plan needs a payment method.")]
    [InlineData("A paid plan needs a payment method.", "A paid plan needs a payment method.")]
    [InlineData("""{"message":"A paid plan needs a payment method."}""", "A paid plan needs a payment method.")]
    [InlineData("""{"title":"One or more validation errors occurred.","errors":{"Name":["Name is required."]}}""", "Name is required.")]
    [InlineData("""["Name is required.","Plan is required."]""", "Name is required. Plan is required.")]
    [InlineData("""[{"message":"A paid plan needs a payment method."}]""", "A paid plan needs a payment method.")]
    public void ABadRequest_KeepsBillingsMessage(string body, string expected) =>
        Remote(400, body).Should().Be(expected);

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(409)]
    [InlineData(500)]
    [InlineData(502)]
    public void AnyOtherFailure_IsTheStatusOnly_NeverTheBody(int status) =>
        Remote(status, """{"error":"secret-detail at Billing.Internal.Service"}""")
            .Should().Be($"The billing API returned status {status}.");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<html><body>Bad Request</body></html>")]
    [InlineData("{not json")]
    [InlineData("""{"unrelated":"field"}""")]
    [InlineData("[]")]
    [InlineData("[1,2,3]")]
    [InlineData("""[{"code":7}]""")]
    [InlineData("[Error] something broke")]
    [InlineData("null")]
    [InlineData("42")]
    public void ABadRequestWithNothingReadable_IsTheStatusOnly(string body) =>
        Remote(400, body).Should().Be("The billing API returned status 400.");

    [Fact]
    public void ABadRequestMessage_IsOneShortLineOfText()
    {
        var message = Remote(400, "line one\n\nline two\u001b[31m" + new string('x', 1000));

        message.Should().StartWith("line one line two").And.NotContain("\n").And.NotContain("\u001b");
        message.Length.Should().BeLessThan(400);
    }

    [Fact]
    public void AnUnreadableSuccess_SaysSoWithoutTheBody() =>
        Remote(200, "garbage-body").Should().Be("The billing API returned a response this command couldn't read.");

    [Fact]
    public async Task TheBillingClient_ReportsFailuresAsBillingExceptions_ThatKeepTheFullBodyForLocalUse()
    {
        var mock = new MockHttpMessageHandler();
        mock.When("https://billing.example/v1/plans").Respond(System.Net.HttpStatusCode.BadRequest, "text/plain", "Invalid plan");
        var client = new BillingClient("https://billing.example", new HttpClient(mock), new HttpClient(mock));

        var act = () => client.GetPlansAsync();

        var thrown = (await act.Should().ThrowAsync<BillingException>()).Which;
        thrown.StatusCode.Should().Be(400);
        thrown.Message.Should().Be("Invalid plan");
    }

    // ── Rule: the caller's token never goes on the public plans call ───────────

    [Fact]
    public async Task TheUnauthenticatedTwin_SendsNoAuthorizationHeader()
    {
        var mock = new MockHttpMessageHandler();
        var anonymous = mock.When("https://billing.example/v1/plans").With(request => request.Headers.Authorization is null).Respond("application/json", "[]");
        var signedIn = new HttpClient(mock);
        signedIn.DefaultRequestHeaders.Authorization = new("Bearer", "callers-token");
        var client = new BillingClient("https://billing.example", signedIn, new HttpClient(mock));

        await client.Unauthenticated().GetPlansAsync();

        mock.GetMatchCount(anonymous).Should().Be(1);
    }

    [Fact]
    public void ABillingClientWithNoAnonymousTwin_RefusesToPretendItIsOne()
    {
        var signedIn = new HttpClient(new MockHttpMessageHandler());
        signedIn.DefaultRequestHeaders.Authorization = new("Bearer", "callers-token");
        var client = new BillingClient(new PlatformConfig { Token = "saved-login" }, signedIn);

        var act = () => client.Unauthenticated();

        act.Should().Throw<InvalidOperationException>();
    }
}
