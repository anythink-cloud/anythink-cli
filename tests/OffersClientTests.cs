using System.Text.Json;
using AnythinkCli.Client;
using AnythinkCli.Models;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

/// <summary>
/// URL/body coverage for the AnythinkPay offers (admin) client surface. Rewards and
/// eligibility travel as opaque JSON strings, so the assertions check the wire field
/// names (snake_case) and the real routes rather than typed reward objects.
/// </summary>
public class OffersClientTests
{
    private const string BaseUrl = "https://api.example.com";
    private const string OrgId   = "99999";
    private const string PayPath = $"{BaseUrl}/org/{OrgId}/integrations/anythinkpay";

    private static AnythinkClient BuildClient(MockHttpMessageHandler handler)
        => new(OrgId, BaseUrl, new HttpClient(handler));

    private const string OfferJson = """
    {"id":"11111111-1111-1111-1111-111111111111","name":"Launch 50","kind":"discount",
     "status":"active","per_user_redemption_cap":1,
     "redeemer_reward_json":"{\"type\":\"discount\",\"percent_off\":50,\"duration\":\"once\"}",
     "created_at":"2026-05-30T10:00:00Z","updated_at":"2026-05-30T10:00:00Z"}
    """;

    [Fact]
    public async Task GetOffersAsync_TargetsOffersUrl()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{PayPath}/offers").Respond("application/json", $"[{OfferJson}]");

        var offers = await BuildClient(handler).GetOffersAsync();

        offers.Should().HaveCount(1);
        offers[0].Name.Should().Be("Launch 50");
        offers[0].RedeemerRewardJson.Should().Contain("percent_off");
    }

    [Fact]
    public async Task GetOfferAsync_TargetsOfferUrl()
    {
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{PayPath}/offers/{id}").Respond("application/json", OfferJson);

        var offer = await BuildClient(handler).GetOfferAsync(id);

        offer!.Kind.Should().Be("discount");
    }

    [Fact]
    public async Task CreateOfferAsync_PostsKindAndRewardJsonSnakeCase()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{PayPath}/offers")
               .WithPartialContent("\"kind\":\"referral\"")
               .WithPartialContent("\"redeemer_reward_json\"")
               .WithPartialContent("\"referrer_reward_json\"")
               .Respond("application/json", OfferJson);

        var act = async () => await BuildClient(handler).CreateOfferAsync(new CreateOfferRequest(
            Name: "Referral", Kind: "referral",
            RedeemerRewardJson: """{"type":"trial_extension","days":14}""",
            ReferrerRewardJson: """{"type":"subscription_extension","days":30}"""));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task CreateOfferAsync_DoesNotSendCentralTenantId()
    {
        string? body = null;
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{PayPath}/offers")
               .With(req => { body = req.Content!.ReadAsStringAsync().Result; return true; })
               .Respond("application/json", OfferJson);

        await BuildClient(handler).CreateOfferAsync(new CreateOfferRequest(
            Name: "X", Kind: "discount", RedeemerRewardJson: """{"type":"discount","percent_off":10}"""));

        body.Should().NotBeNull();
        body!.Should().NotContain("central_tenant_id");
    }

    [Fact]
    public async Task UpdateOfferAsync_PatchesStatus()
    {
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Put, $"{PayPath}/offers/{id}")
               .WithPartialContent("\"status\":\"paused\"")
               .Respond("application/json", OfferJson);

        var act = async () => await BuildClient(handler).UpdateOfferAsync(id, new UpdateOfferRequest(Status: "paused"));

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task SetOfferStatusAsync_PutsStatusOnly()
    {
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Put, $"{PayPath}/offers/{id}")
               .WithPartialContent("\"status\":\"active\"")
               .Respond("application/json", OfferJson);

        var act = async () => await BuildClient(handler).SetOfferStatusAsync(id, "active");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetOfferCodesAsync_TargetsCodesUrl()
    {
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{PayPath}/offers/{id}/codes")
               .Respond("application/json",
                   """[{"id":"22222222-2222-2222-2222-222222222222","offer_id":"11111111-1111-1111-1111-111111111111","slug":"LAUNCH50","redemption_count":3,"created_at":"2026-05-30T10:00:00Z"}]""");

        var codes = await BuildClient(handler).GetOfferCodesAsync(id);

        codes.Should().ContainSingle();
        codes[0].Slug.Should().Be("LAUNCH50");
    }

    [Fact]
    public async Task CreateOfferCodeAsync_PostsSlugAndOwner()
    {
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Post, $"{PayPath}/offers/{id}/codes")
               .WithPartialContent("\"slug\":\"LAUNCH50\"")
               .WithPartialContent("\"owner_user_id\":7")
               .Respond("application/json",
                   """{"id":"22222222-2222-2222-2222-222222222222","offer_id":"11111111-1111-1111-1111-111111111111","slug":"LAUNCH50","owner_user_id":7,"redemption_count":0,"created_at":"2026-05-30T10:00:00Z"}""");

        var code = await BuildClient(handler).CreateOfferCodeAsync(id, new CreateOfferCodeRequest("LAUNCH50", 7));

        code.Slug.Should().Be("LAUNCH50");
        code.OwnerUserId.Should().Be(7);
    }

    [Fact]
    public async Task GetOfferRedemptionsAsync_ReturnsPlainListWithPaging()
    {
        var id = Guid.NewGuid();
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{PayPath}/offers/{id}/redemptions*")
               .WithQueryString("page", "2")
               .WithQueryString("pageSize", "10")
               .Respond("application/json",
                   """[{"id":"33333333-3333-3333-3333-333333333333","offer_id":"11111111-1111-1111-1111-111111111111","code_id":"22222222-2222-2222-2222-222222222222","redeemer_user_id":7,"redeemer_reward_status":"applied","redeemed_at":"2026-05-30T10:00:00Z"}]""");

        var rows = await BuildClient(handler).GetOfferRedemptionsAsync(id, page: 2, pageSize: 10);

        rows.Should().ContainSingle();
        rows[0].RedeemerRewardStatus.Should().Be("applied");
    }

    [Fact]
    public async Task GetUserCodeAsync_TargetsUserCodeUrl()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{PayPath}/offers/users/42/code")
               .Respond("application/json",
                   """{"slug":"CHRIS-A7K2","offer_id":"11111111-1111-1111-1111-111111111111","redemption_count":2,"offer_name":"Referral"}""");

        var code = await BuildClient(handler).GetUserCodeAsync(42);

        code!.Slug.Should().Be("CHRIS-A7K2");
        code.RedemptionCount.Should().Be(2);
    }

    // ── Serialization round-trips ─────────────────────────────────────────────

    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void OfferResponse_ReadsRewardJsonAsString()
    {
        var offer = JsonSerializer.Deserialize<OfferResponse>(OfferJson, Opts)!;

        offer.Kind.Should().Be("discount");
        offer.RedeemerRewardJson.Should().Be("""{"type":"discount","percent_off":50,"duration":"once"}""");
    }

    [Fact]
    public void OfferRewardDto_RoundTripsTieredShape()
    {
        const string json = """
        {"type":"tiered","tiers":[{"at":1,"reward":{"type":"trial_extension","days":7}},
                                  {"at":3,"reward":{"type":"subscription_extension","days":30}}]}
        """;
        var reward = JsonSerializer.Deserialize<OfferRewardDto>(json, Opts)!;

        reward.Type.Should().Be("tiered");
        reward.Tiers.Should().HaveCount(2);
        reward.Tiers![1].At.Should().Be(3);
        reward.Tiers[1].Reward.Days.Should().Be(30);
    }
}
