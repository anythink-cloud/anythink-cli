using AnythinkCli.Client;
using AnythinkCli.Commands;
using AnythinkCli.Models;
using Spectre.Console;
using Spectre.Console.Rendering;
using FluentAssertions;
using RichardSzalay.MockHttp;

namespace AnythinkCli.Tests;

[Collection("ConsoleOutput")]
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

    [Fact]
    public async Task PlanPicker_FractionalStorage_ShowsHalfAGigabyte()
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{BillingUrl}/v1/plans").Respond("application/json", PlansJson);
        var http = new HttpClient(handler);

        var plans = await new BillingClient(BillingUrl, http, http).GetPlansAsync();

        plans[0].PickerLabel.Should().Be("Free — Free | 0.5GB | 3 users");
    }

    private static BillingClient BillingWith(string plansJson)
    {
        var handler = new MockHttpMessageHandler();
        handler.When(HttpMethod.Get, $"{BillingUrl}/v1/plans").Respond("application/json", plansJson);
        var http = new HttpClient(handler);
        return new BillingClient(BillingUrl, http, http);
    }

    [Fact]
    public async Task PlansList_FractionalStorageQuota_ShowsHalfAGigabyte()
    {
        ClientContext.Billing = BillingWith(PlansJson);
        try
        {
            var (code, output) = await CommandRunner.RunAsync(new MockHttpMessageHandler(),
                () => new PlansListCommand().ExecuteAsync(null!, new PlansListSettings()));

            code.Should().Be(0);
            output.Should().Contain("0.5GB").And.Contain("Free");
        }
        finally { ClientContext.Billing = null; }
    }

    [Fact]
    public async Task ProjectsCreate_PlanPickerWithFractionalStorage_ListsThePlan()
    {
        ClientContext.Billing = BillingWith(PlansJson);
        try
        {
            var (_, output) = await CommandRunner.RunAsync(new MockHttpMessageHandler(),
                () => new ProjectsCreateCommand().ExecuteAsync(null!, new ProjectsCreateSettings { Name = "demo", Region = "lon1" }),
                interactive: true);

            output.Should().Contain("Free — Free | 0.5GB | 3 users");
            output.Should().NotContain("Could not fetch plans");
        }
        finally { ClientContext.Billing = null; }
    }
}
