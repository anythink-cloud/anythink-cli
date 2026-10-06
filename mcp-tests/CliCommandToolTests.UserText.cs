using System.Text;
using AnythinkMcp.Cli;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkMcp.Tests;

// ── Rule: a value containing square brackets is shown as typed, never parsed as markup ──
// Spinner text is parsed on Spectre's refresh thread, so an unescaped value there kills the host
// once the API takes longer than the first refresh. A message line throws after the change was applied.

public partial class CliCommandToolTests
{
    private static readonly TimeSpan SlowApi = TimeSpan.FromMilliseconds(250);

    private static List<(HttpMethod Method, string Path, string Body)> RespondSlowly(
        MockHttpMessageHandler mock, Func<string, string> jsonForPath)
    {
        var sent = new List<(HttpMethod, string, string)>();
        mock.When("*").Respond(async req =>
        {
            var body = req.Content is null ? "" : await req.Content.ReadAsStringAsync();
            lock (sent) sent.Add((req.Method, req.RequestUri!.AbsolutePath, body));
            await Task.Delay(SlowApi);
            return new HttpResponseMessage { Content = new StringContent(jsonForPath(req.RequestUri.AbsolutePath), Encoding.UTF8, "application/json") };
        });
        return sent;
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task RolesCreate_NameWithBrackets_SucceedsOnceAndIsShownLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var sent = RespondSlowly(mock, _ => """{"id":5,"name":"Shop [beta]","is_active":true}""");

        var result = await Tool("roles_create", scope).RunAsync(Args(new { name = "Shop [beta]" }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("Shop [beta]");
        sent.Should().ContainSingle().Which.Path.Should().Be("/org/42/roles");
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task UsersInvite_EmailWithBrackets_SucceedsAndEachRequestIsSentOnce(CliToolScope scope)
    {
        const string email = "ann[1]@example.com";
        var mock = new MockHttpMessageHandler();
        var sent = RespondSlowly(mock, path => path.EndsWith("/send-invitation-email")
            ? "{}"
            : $$"""{"id":9,"first_name":"Ann","last_name":"Lee","email":"{{email}}","is_confirmed":false,"created_at":"2026-01-01T00:00:00Z"}""");

        var result = await Tool("users_invite", scope).RunAsync(
            Args(new { email, first_name = "Ann", last_name = "Lee" }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain(email);
        sent.Select(s => s.Path).Should().Equal("/org/42/users", "/org/42/users/9/send-invitation-email");
    }

    [Fact]
    public async Task EntitiesCreate_NameWithBrackets_SucceedsOnceAndIsShownLiterally()
    {
        var mock = new MockHttpMessageHandler();
        var sent = RespondSlowly(mock, _ =>
            """{"name":"Shop [beta]","table_name":"shop_beta","enable_rls":false,"is_system":false,"is_junction":false,"is_public":false,"lock_new_records":false,"fields":[],"id":3}""");

        var result = await Tool("entities_create", CliToolScope.Local).RunAsync(Args(new { name = "Shop [beta]" }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("Shop [beta]");
        sent.Should().ContainSingle().Which.Path.Should().Be("/org/42/entities");
    }

    [Fact]
    public async Task SecretsCreate_KeyWithBrackets_SucceedsOnceAndIsShownLiterally()
    {
        var mock = new MockHttpMessageHandler();
        var sent = RespondSlowly(mock, _ =>
            """{"id":1,"key":"API [key]","created_at":"2026-01-01T00:00:00Z","updated_at":"2026-01-01T00:00:00Z","users":[]}""");

        var result = await Tool("secrets_create", CliToolScope.Local).RunAsync(Args(new { key = "API [key]", value = "s3cret" }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("API [key]");
        sent.Should().ContainSingle().Which.Path.Should().Be("/org/42/secrets");
    }

    // ── The change is applied before the success line is printed, so a throw there reports a failure for work that happened ──

    [Fact]
    public async Task DataRls_EntityWithBrackets_ReportsSuccessAfterTheChangeIsApplied()
    {
        var mock = new MockHttpMessageHandler();
        var sent = RespondSlowly(mock, _ => "[]");

        var result = await Tool("data_rls", CliToolScope.Local).RunAsync(
            Args(new { entity = "shop[beta]", id = 1, user = 7 }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("shop[beta]");
        sent.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Put);
    }

    [Theory]
    [MemberData(nameof(RemoteScopes))]
    public async Task IntegrationsTest_ServerMessageWithBrackets_IsShownLiterally(CliToolScope scope)
    {
        var mock = new MockHttpMessageHandler();
        var sent = RespondSlowly(mock, _ => """{"success":true,"message":"Connected to [beta] workspace"}""");

        var result = await Tool("integrations_test", scope).RunAsync(Args(new { connection_id = "abc" }), Client(mock));

        result.ExitCode.Should().Be(0, result.Output);
        result.Output.Should().Contain("Connected to [beta] workspace");
        sent.Should().ContainSingle();
    }
}
