using AnythinkCli.Config;
using FluentAssertions;
using System.Text;

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
    public void IsTokenExpired_WhenNonJwtTokenAndNoStoredExpiry_ReturnsFalse()
    {
        // Non-JWT opaque token — can't be decoded, no stored expiry → not expired (fallback).
        var profile = new Profile { AccessToken = "opaque-api-token-not-a-jwt" };
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

    // ── JWT exp-claim based expiry ────────────────────────────────────────────
    // These tests verify that IsTokenExpired reads the exp claim from the JWT
    // payload directly, rather than relying on TokenExpiresAt being stored.

    /// <summary>Creates a minimal JWT with a real base64url-encoded payload.</summary>
    private static string MakeJwt(long unixExp)
    {
        var header  = B64Url("{\"alg\":\"HS256\"}");
        var payload = B64Url($"{{\"exp\":{unixExp},\"sub\":\"test-user\"}}");
        return $"{header}.{payload}.fakesignature";
    }

    private static string MakeJwtNoExp()
    {
        var header  = B64Url("{\"alg\":\"HS256\"}");
        var payload = B64Url("{\"sub\":\"test-user\"}");
        return $"{header}.{payload}.fakesignature";
    }

    private static string B64Url(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static long UnixNow()   => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    private static long PastUnix()  => UnixNow() - 3600;   // 1 hour ago
    private static long FutureUnix() => UnixNow() + 3600;  // 1 hour from now

    [Fact]
    public void IsTokenExpired_JwtWithExpiredClaim_ReturnsTrue_EvenWithNoStoredExpiry()
    {
        var profile = new Profile { AccessToken = MakeJwt(PastUnix()) };
        // No TokenExpiresAt stored — previously this would return false (the bug).
        profile.IsTokenExpired.Should().BeTrue();
    }

    [Fact]
    public void IsTokenExpired_JwtWithFutureClaim_ReturnsFalse_EvenWithNoStoredExpiry()
    {
        var profile = new Profile { AccessToken = MakeJwt(FutureUnix()) };
        profile.IsTokenExpired.Should().BeFalse();
    }

    [Fact]
    public void IsTokenExpired_JwtExpClaim_TakesPrecedenceOverStoredExpiry_WhenJwtExpired()
    {
        // JWT says expired, stored expiry says future → JWT wins.
        var profile = new Profile
        {
            AccessToken    = MakeJwt(PastUnix()),
            TokenExpiresAt = DateTime.UtcNow.AddHours(10)
        };
        profile.IsTokenExpired.Should().BeTrue();
    }

    [Fact]
    public void IsTokenExpired_JwtExpClaim_TakesPrecedenceOverStoredExpiry_WhenJwtValid()
    {
        // JWT says future, stored expiry says past → JWT wins, not expired.
        var profile = new Profile
        {
            AccessToken    = MakeJwt(FutureUnix()),
            TokenExpiresAt = DateTime.UtcNow.AddHours(-10)
        };
        profile.IsTokenExpired.Should().BeFalse();
    }

    [Fact]
    public void IsTokenExpired_JwtWithNoExpClaim_FallsBackToStoredExpiry_WhenPast()
    {
        var profile = new Profile
        {
            AccessToken    = MakeJwtNoExp(),
            TokenExpiresAt = DateTime.UtcNow.AddHours(-1)
        };
        profile.IsTokenExpired.Should().BeTrue();
    }

    [Fact]
    public void IsTokenExpired_JwtWithNoExpClaim_FallsBackToStoredExpiry_WhenFuture()
    {
        var profile = new Profile
        {
            AccessToken    = MakeJwtNoExp(),
            TokenExpiresAt = DateTime.UtcNow.AddHours(1)
        };
        profile.IsTokenExpired.Should().BeFalse();
    }

    [Fact]
    public void IsTokenExpired_JwtWithNoExpClaim_AndNoStoredExpiry_ReturnsFalse()
    {
        // No exp claim, no stored expiry — token is assumed valid (can't know otherwise).
        var profile = new Profile { AccessToken = MakeJwtNoExp() };
        profile.IsTokenExpired.Should().BeFalse();
    }
}
