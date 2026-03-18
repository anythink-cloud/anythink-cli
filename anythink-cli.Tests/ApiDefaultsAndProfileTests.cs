using AnythinkCli.Config;
using FluentAssertions;

namespace AnythinkCli.Tests;

// ── ApiDefaults ───────────────────────────────────────────────────────────────

public class ApiDefaultsTests
{
    [Fact]
    public void ForEnv_DefaultOrEmpty_ReturnsProdUrls()
    {
        var (api, billing, orgId) = ApiDefaults.ForEnv("");
        api.Should().Be(ApiDefaults.ProdApi);
        billing.Should().Be(ApiDefaults.BillingCloud);
        orgId.Should().Be(ApiDefaults.PlatformOrgIdProd);
    }

    [Fact]
    public void ForEnv_AnyUnknownValue_ReturnsProdUrls()
    {
        var (api, billing, _) = ApiDefaults.ForEnv("production");
        api.Should().Be(ApiDefaults.ProdApi);
        billing.Should().Be(ApiDefaults.BillingCloud);
    }

    [Fact]
    public void ForEnv_Dev_ReturnsDevUrls()
    {
        var (api, billing, _) = ApiDefaults.ForEnv("dev");
        api.Should().Be(ApiDefaults.DevApi);
        billing.Should().Be(ApiDefaults.BillingDev);
    }

    [Fact]
    public void ForEnv_Local_ReturnsLocalUrls()
    {
        var (api, billing, _) = ApiDefaults.ForEnv("local");
        api.Should().Be(ApiDefaults.LocalApi);
        billing.Should().Be(ApiDefaults.LocalBilling);
    }

    [Theory]
    [InlineData("DEV")]
    [InlineData("Dev")]
    [InlineData("dEv")]
    public void ForEnv_Dev_IsCaseInsensitive(string env)
    {
        var (api, _, _) = ApiDefaults.ForEnv(env);
        api.Should().Be(ApiDefaults.DevApi);
    }

    [Theory]
    [InlineData("LOCAL")]
    [InlineData("Local")]
    [InlineData("lOcAl")]
    public void ForEnv_Local_IsCaseInsensitive(string env)
    {
        var (api, _, _) = ApiDefaults.ForEnv(env);
        api.Should().Be(ApiDefaults.LocalApi);
    }

    [Fact]
    public void ProdApi_HasExpectedSchemeAndHost()
    {
        ApiDefaults.ProdApi.Should().StartWith("https://");
        ApiDefaults.ProdApi.Should().Contain("anythink");
    }

    [Fact]
    public void ProdOrgId_IsNotEmpty()
    {
        ApiDefaults.PlatformOrgIdProd.Should().NotBeNullOrEmpty();
    }
}

// ── Profile.IsTokenExpired ────────────────────────────────────────────────────

public class ProfileTests
{
    [Fact]
    public void IsTokenExpired_WhenApiKeySet_ReturnsFalse()
    {
        var profile = new Profile { ApiKey = "ak_test123" };
        profile.IsTokenExpired.Should().BeFalse();
    }

    [Fact]
    public void IsTokenExpired_WhenNoTokenOrKey_ReturnsTrue()
    {
        var profile = new Profile();
        profile.IsTokenExpired.Should().BeTrue();
    }

    [Fact]
    public void IsTokenExpired_WhenTokenSetAndNoExpiry_ReturnsFalse()
    {
        var profile = new Profile { AccessToken = "eyJtest.token.here" };
        profile.IsTokenExpired.Should().BeFalse();
    }

    [Fact]
    public void IsTokenExpired_WhenTokenSetAndFutureExpiry_ReturnsFalse()
    {
        var profile = new Profile
        {
            AccessToken    = "eyJtest.token.here",
            TokenExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        profile.IsTokenExpired.Should().BeFalse();
    }

    [Fact]
    public void IsTokenExpired_WhenTokenSetAndPastExpiry_ReturnsTrue()
    {
        var profile = new Profile
        {
            AccessToken    = "eyJtest.token.here",
            TokenExpiresAt = DateTime.UtcNow.AddHours(-1)
        };
        profile.IsTokenExpired.Should().BeTrue();
    }

    [Fact]
    public void IsTokenExpired_ApiKeyIgnoresExpiredToken()
    {
        // API key auth should never be considered expired regardless of token state
        var profile = new Profile
        {
            ApiKey         = "ak_test123",
            AccessToken    = "eyJtest.token.here",
            TokenExpiresAt = DateTime.UtcNow.AddHours(-1)
        };
        profile.IsTokenExpired.Should().BeFalse();
    }

    [Fact]
    public void Profile_DefaultBaseUrl_IsProductionApi()
    {
        var profile = new Profile();
        profile.BaseUrl.Should().Be(ApiDefaults.ProdApi);
    }
}
