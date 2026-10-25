using AnythinkCli.Client;
using AnythinkCli.Models;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

public class BillingPlansTests
{
    private const string BillingUrl = "https://billing.example.com";

    private const string PlansJson = """
    [{"id":"11111111-1111-1111-1111-111111111111","name":"Free","description":null,"resource":0,
      "monthly_price_cents":0,"annual_price_cents":0,"currency":"gbp",
      "storage_quota_gb":0.5,"file_quota_gb":1.5,"user_quota":3,
      "custom_domain_enabled":false,"backups_enabled":false,"priority_support":false,
      "is_active":true,"display_order":1}]
    """;

    [Fact]
    public async Task Plans_FractionalStorageQuota_Parses()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{BillingUrl}/v1/plans").Respond("application/json", PlansJson);
        var http = new HttpClient(handler);

        var plans = await new BillingClient(BillingUrl, http, http).GetPlansAsync();

        plans.Should().ContainSingle();
        plans[0].StorageQuotaGb.Should().Be(0.5m);
        plans[0].FileQuotaGb.Should().Be(1.5m);
    }

    [Theory]
    [InlineData(0.5, "0.5GB")]
    [InlineData(10, "10GB")]
    [InlineData(10.0, "10GB")]
    public void FormatGb_DropsTrailingZeros(double gb, string expected)
        => BillingPlan.FormatGb((decimal)gb).Should().Be(expected);
}
